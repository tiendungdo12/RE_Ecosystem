using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace RE_CommandCenter.Core
{
    public class OllamaClient
    {
        private readonly HttpClient _httpClient;
        private const string Endpoint = "http://localhost:11434/api/generate";

        public OllamaClient()
        {
            _httpClient = new HttpClient();
        }

        // Hop den goi model local:
        // Input: prompt nguoi dung + systemPrompt tu AgentConfigs.
        // Output: tung chunk text duoc yield ve UI de tao hieu ung streaming.
        public async IAsyncEnumerable<string> GenerateStreamAsync(string prompt, string systemPrompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                model = "LFM25_1B", // LFM 1.2B model for faster local inference
                prompt = prompt,
                system = systemPrompt,
                stream = true
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = jsonContent
            };

            HttpResponseMessage? response = null;
            string? errorMessage = null;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                errorMessage = $"[Error connecting to Ollama: {ex.Message}]";
            }

            if (errorMessage != null || response == null)
            {
                yield return errorMessage ?? "[Unknown Error]";
                yield break;
            }

            // Ollama tra ve nhieu dong JSON rieng le.
            // Moi dong co the chua mot phan "response", nen ta doc tung line va yield tung chunk.
            using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(responseStream);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;
                
                if (string.IsNullOrWhiteSpace(line)) continue;

                OllamaResponse? chunk = null;
                try
                {
                    chunk = JsonSerializer.Deserialize<OllamaResponse>(line);
                }
                catch (JsonException)
                {
                    // Bo qua dong loi de stream UI khong bi dung vi mot chunk hong.
                }

                if (chunk != null && !string.IsNullOrEmpty(chunk.Response))
                {
                    yield return chunk.Response;
                }
            }
        }
        
        private class OllamaResponse
        {
            [JsonPropertyName("response")]
            public string? Response { get; set; }

            [JsonPropertyName("done")]
            public bool Done { get; set; }
        }
    }
}
