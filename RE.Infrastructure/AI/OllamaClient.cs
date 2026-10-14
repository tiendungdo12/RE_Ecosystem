using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace RE.Infrastructure.AI
{
    public class OllamaClient
    {
        private readonly HttpClient _httpClient;
        private readonly OllamaUsageStore _usageStore;

        public OllamaClient()
        {
            _httpClient = new HttpClient();
            _usageStore = new OllamaUsageStore();
        }

        public Task<OllamaUsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default)
            => _usageStore.GetSnapshotAsync(cancellationToken);

        // Hop den goi model local:
        // Input: prompt nguoi dung + systemPrompt tu AgentConfigs.
        // Output: tung chunk text duoc yield ve UI de tao hieu ung streaming.
        public async IAsyncEnumerable<string> GenerateStreamAsync(
            string prompt,
            string systemPrompt,
            string endpoint = "http://localhost:11434/api/generate",
            string modelName = "LFM25_1B",
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                model = modelName,
                prompt = prompt,
                system = systemPrompt,
                stream = true
            };

            var jsonContent =
                new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            string apiEndpoint = endpoint;
            if (!apiEndpoint.EndsWith("/api/generate") && !apiEndpoint.EndsWith("/api/chat"))
            {
                apiEndpoint = apiEndpoint.TrimEnd('/') + "/api/generate";
            }

            var request = new HttpRequestMessage(HttpMethod.Post, apiEndpoint)
            {
                Content = jsonContent
            };

            HttpResponseMessage? response = null;
            string? errorMessage = null;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    errorMessage =
                        "[Ollama Cloud: Tài khoản của bạn đã đạt giới hạn hạn mức (Rate limit 429). Vui lòng kiểm tra lại trên ollama.com/settings]";
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    await _usageStore.RecordRequestAsync(modelName, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                errorMessage = errorMessage ?? $"[Error connecting to Ollama: {ex.Message}]";
            }

            if (errorMessage != null || response == null)
            {
                yield return errorMessage ?? "[Unknown Error]";
                yield break;
            }

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
            [JsonPropertyName("response")] public string? Response { get; set; }

            [JsonPropertyName("done")] public bool Done { get; set; }
        }

        public async Task<List<string>> GetModelsAsync(string port = "11434",
            CancellationToken cancellationToken = default)
        {
            var models = new List<string>();
            try
            {
                // Model discovery is UI metadata, not a request that should
                // wait indefinitely while the local service starts up.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var response = await _httpClient.GetAsync($"http://localhost:{port}/api/tags", timeout.Token);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<JsonElement>(json);
                if (root.TryGetProperty("models", out var modelsArray))
                {
                    foreach (var modelElement in modelsArray.EnumerateArray())
                    {
                        if (modelElement.TryGetProperty("name", out var nameElement))
                        {
                            models.Add(nameElement.GetString() ?? "");
                        }
                    }
                }
            }
            catch
            {
                // Return empty list on failure
            }

            return models;
        }

        public async Task<List<OllamaModelInfo>> GetInstalledModelsDetailsAsync(string port = "11434",
            CancellationToken cancellationToken = default)
        {
            var models = new List<OllamaModelInfo>();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var response = await _httpClient.GetAsync($"http://localhost:{port}/api/tags", timeout.Token);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<JsonElement>(json);
                if (root.TryGetProperty("models", out var modelsArray))
                {
                    foreach (var modelElement in modelsArray.EnumerateArray())
                    {
                        string name = modelElement.TryGetProperty("name", out var nameElement)
                            ? nameElement.GetString() ?? ""
                            : "";
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        long size = 0;
                        if (modelElement.TryGetProperty("size", out var sizeElement))
                        {
                            size = sizeElement.GetInt64();
                        }

                        DateTime? modifiedAt = null;
                        if (modelElement.TryGetProperty("modified_at", out var modElement) &&
                            DateTime.TryParse(modElement.GetString(), out var dt))
                        {
                            modifiedAt = dt;
                        }

                        models.Add(new OllamaModelInfo
                        {
                            Name = name,
                            Size = size,
                            SizeFormatted = FormatSize(size),
                            ModifiedAt = modifiedAt
                        });
                    }
                }
            }
            catch
            {
                // Return empty list on failure
            }

            return models;
        }

        public async Task<bool> DeleteModelAsync(string modelName, string port = "11434",
            CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, $"http://localhost:{port}/api/delete")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { model = modelName }), Encoding.UTF8,
                        "application/json")
                };
                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode) return true;
            }
            catch
            {
            }

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = $"rm {modelName}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync(cancellationToken);
                    return process.ExitCode == 0;
                }
            }
            catch
            {
            }

            return false;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "";
            double gb = bytes / (1024.0 * 1024.0 * 1024.0);
            if (gb >= 1.0)
                return $"{gb:0.1} GB";
            double mb = bytes / (1024.0 * 1024.0);
            return $"{mb:0.1} MB";
        }
    }

    public class OllamaModelInfo
    {
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public string SizeFormatted { get; set; } = string.Empty;
        public DateTime? ModifiedAt { get; set; }
    }
}