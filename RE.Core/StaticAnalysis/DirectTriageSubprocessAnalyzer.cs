using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RE.Core.Abstractions;

namespace RE.Core.StaticAnalysis
{
    /// <summary>
    /// Analyzer invoking LIEF and AXML parsing directly via Python subprocess.
    /// Does NOT use MCP, FastMCP, or LLM.
    /// </summary>
    public interface IDirectTriageSubprocessAnalyzer
    {
        Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default);

        Task<JsonElement> AnalyzeAsync(string filePath, string? md5, string? sha256, CancellationToken cancellationToken = default)
            => AnalyzeAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// Direct subprocess execution of direct_triage.py.
    /// </summary>
    public class DirectTriageSubprocessAnalyzer : IDirectTriageSubprocessAnalyzer
    {
        private readonly IPlatformEnvironment _platform;

        /// <summary>
        /// Optional diagnostic hook invoked immediately after the child process is launched.
        /// </summary>
        public Action<int>? OnProcessStarted { get; set; }

        /// <summary>
        /// Optional script path override for testing or custom runner scripts.
        /// </summary>
        public string? ScriptPathOverride { get; set; }

        /// <summary>
        /// Configurable execution timeout. Defaults to 15 seconds.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

        public DirectTriageSubprocessAnalyzer(IPlatformEnvironment platform)
        {
            _platform = platform;
        }

        public Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return AnalyzeAsync(filePath, null, null, cancellationToken);
        }

        public async Task<JsonElement> AnalyzeAsync(string filePath, string? md5, string? sha256, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return CreateErrorJson("Target file does not exist.");
            }

            string pythonExe = _platform.PythonExecutable;
            if (!File.Exists(pythonExe))
            {
                return CreateErrorJson($"Python executable not found at: {pythonExe}");
            }

            string scriptPath = ScriptPathOverride ?? ResolveScriptPath();
            if (!File.Exists(scriptPath))
            {
                return CreateErrorJson($"Direct triage script not found at: {scriptPath}");
            }

            var argsBuilder = new StringBuilder();
            argsBuilder.Append($"\"{scriptPath}\" \"{filePath}\"");
            if (!string.IsNullOrEmpty(md5) && !string.IsNullOrEmpty(sha256) && md5 != "N/A" && sha256 != "N/A")
            {
                argsBuilder.Append($" --md5 \"{md5}\" --sha256 \"{sha256}\"");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = argsBuilder.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
                OnProcessStarted?.Invoke(process.Id);

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                using var timeoutCts = new CancellationTokenSource(Timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                        }
                    }
                    catch
                    {
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("Deterministic triage subprocess cancelled by caller.", cancellationToken);
                    }

                    return CreateErrorJson($"Subprocess triage timed out after {Timeout.TotalSeconds} seconds.", errorCode: "TIMEOUT", isTimeout: true);
                }

                string stdout = await stdoutTask;
                string stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    string errorDetail = string.IsNullOrWhiteSpace(stderr)
                        ? $"Subprocess exited with code {process.ExitCode}"
                        : $"Subprocess exited with code {process.ExitCode}: {stderr.Trim()}";

                    if (string.IsNullOrWhiteSpace(stdout))
                    {
                        return CreateErrorJson(errorDetail, errorCode: "SUBPROCESS_ERROR");
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(stdout);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            using var stream = new MemoryStream();
                            using (var writer = new Utf8JsonWriter(stream))
                            {
                                writer.WriteStartObject();
                                foreach (var prop in doc.RootElement.EnumerateObject())
                                {
                                    if (prop.NameEquals("error") || prop.NameEquals("exit_code") || prop.NameEquals("error_code"))
                                    {
                                        continue;
                                    }
                                    prop.WriteTo(writer);
                                }
                                writer.WriteString("error", errorDetail);
                                writer.WriteString("error_code", "SUBPROCESS_ERROR");
                                writer.WriteNumber("exit_code", process.ExitCode);
                                writer.WriteEndObject();
                            }
                            stream.Position = 0;
                            using var mergedDoc = JsonDocument.Parse(stream);
                            return mergedDoc.RootElement.Clone();
                        }
                        else
                        {
                            return CreateErrorJson(errorDetail, errorCode: "SUBPROCESS_ERROR");
                        }
                    }
                    catch (JsonException ex)
                    {
                        return CreateErrorJson(
                            $"Subprocess exited with code {process.ExitCode}. Failed to parse output JSON: {ex.Message}. Output was: {stdout}",
                            errorCode: "SUBPROCESS_ERROR");
                    }
                }

                try
                {
                    using var doc = JsonDocument.Parse(stdout);
                    return doc.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    return CreateErrorJson(
                        $"Failed to parse subprocess output JSON: {ex.Message}. Output was: {stdout}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                }

                throw;
            }
            catch (Exception ex)
            {
                return CreateErrorJson($"Subprocess execution error: {ex.Message}");
            }
        }

        private string ResolveScriptPath()
        {
            // 1. Check relative to PythonExecutable directory
            string? pyDir = Path.GetDirectoryName(_platform.PythonExecutable);
            if (!string.IsNullOrEmpty(pyDir))
            {
                string? parent = Directory.GetParent(pyDir)?.FullName;
                if (!string.IsNullOrEmpty(parent))
                {
                    string candidate = Path.Combine(parent, @"mcp_server\direct_triage.py");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            // 2. Check by walking up from current AppDomain BaseDirectory
            string? cur = AppDomain.CurrentDomain.BaseDirectory;
            while (cur != null)
            {
                string candidate = Path.Combine(cur, @"mcp_server\direct_triage.py");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                cur = Directory.GetParent(cur)?.FullName;
            }

            // Fallback
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                @"mcp_server\direct_triage.py"));
        }

        private static JsonElement CreateErrorJson(string message, string errorCode = "ERROR", bool isTimeout = false)
        {
            var fallback = new Dictionary<string, object?>
            {
                ["exists"] = false,
                ["error"] = message,
                ["error_code"] = errorCode,
                ["is_timeout"] = isTimeout,
                ["recommended_pipeline"] = isTimeout ? "error.timeout" : "error.triage_failed"
            };
            return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(fallback));
        }
    }
}