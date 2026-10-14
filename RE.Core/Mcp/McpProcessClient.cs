using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RE.Core.Mcp
{
    [Obsolete("Sử dụng McpClientManager + McpClientFactory thay thế. McpProcessClient sẽ được loại bỏ dần.")]
    public sealed class McpProcessClient : IAsyncDisposable
    {
        private readonly McpToolConfig _config;
        private readonly string _protocolVersion;
        private readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);
        private Process? _process;
        private int _nextId;

        public McpProcessClient(McpToolConfig config, string protocolVersion)
        {
            _config = config;
            _protocolVersion = protocolVersion;
        }

        // Mo ket noi MCP stdio:
        // 1. spawn process server
        // 2. gui initialize
        // 3. gui notifications/initialized de server biet client da san sang
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            StartProcess();

            await SendRequestAsync(
                "initialize",
                new
                {
                    protocolVersion = _protocolVersion,
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "RE_CommandCenter",
                        version = "0.1.0"
                    }
                },
                null,
                cancellationToken);

            await SendNotificationAsync("notifications/initialized", new { }, cancellationToken);
        }

        // Goi tools/list de kiem tra server dang expose nhung tool nao.
        // Day la buoc discovery truoc khi cho app/AI goi tool that.
        public async Task<JsonElement> ListToolsAsync(CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync("tools/list", new { }, null, cancellationToken);
        }

        // Goi tools/call voi ten tool va arguments dang object.
        // Output tra ve la JsonElement de tang sau tu quyet dinh parse theo tung tool.
        public async Task<JsonElement> CallToolAsync(string toolName, object arguments,
            Action<string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            PreH0ToolPolicy.EnsureAllowed(_config.Id, toolName);
            return await SendRequestAsync(
                "tools/call",
                new
                {
                    name = toolName,
                    arguments
                },
                onProgress,
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_process == null)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }

                await _process.WaitForExitAsync();
            }
            catch
            {
                // Best effort cleanup for short-lived tool calls.
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        // Khoi dong MCP server nhu mot sidecar process.
        // stdout/stdin la kenh JSON-RPC, stderr chi dung cho log chan doan.
        private void StartProcess()
        {
            if (_process != null)
            {
                return;
            }

            string appRoot = AgentConfigLoader.GetAppRootPath();
            string workingDirectory = Path.GetFullPath(Path.Combine(appRoot, _config.WorkingDirectory));

            var startInfo = new ProcessStartInfo
            {
                FileName = _config.Command,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = System.Text.Encoding.UTF8,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                CreateNoWindow = true
            };

            foreach (string arg in _config.Args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            _process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            if (!_process.Start())
            {
                throw new InvalidOperationException($"Failed to start MCP process: {_config.Command}");
            }

            // Doc stderr rieng de log cua Python khong lam ban stdout JSON-RPC.
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_process.HasExited)
                    {
                        string? line = await _process.StandardError.ReadLineAsync();
                        if (line == null)
                        {
                            break;
                        }

                        Debug.WriteLine($"MCP stderr [{_config.Id}]: {line}");
                    }
                }
                catch
                {
                    // Diagnostics only.
                }
            });
        }

        // Notification khong co id va khong doi response.
        // MCP dung kieu nay cho cac tin hieu lifecycle nhu initialized.
        private async Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken)
        {
            EnsureRunning();

            var message = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters
            };

            string json = JsonSerializer.Serialize(message);
            await _process!.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }

        // Request co id va phai doi response cung id.
        // Semaphore giu cho moi process client chi co mot request doc stdout tai mot thoi diem.
        private async Task<JsonElement> SendRequestAsync(string method, object parameters,
            Action<string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            EnsureRunning();

            await _requestLock.WaitAsync(cancellationToken);
            try
            {
                int id = Interlocked.Increment(ref _nextId);
                var message = new Dictionary<string, object?>
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = parameters
                };

                string json = JsonSerializer.Serialize(message);
                await _process!.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
                await _process.StandardInput.FlushAsync(cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    string? line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
                    if (line == null)
                    {
                        throw new InvalidOperationException($"MCP process exited before responding to {method}.");
                    }

                    using JsonDocument document = JsonDocument.Parse(line);
                    JsonElement root = document.RootElement;

                    if (!root.TryGetProperty("id", out JsonElement responseId) || responseId.GetInt32() != id)
                    {
                        // Check if it's a notification/message (progress update)
                        if (onProgress != null && root.TryGetProperty("method", out JsonElement outMethod) &&
                            outMethod.GetString() == "notifications/message")
                        {
                            if (root.TryGetProperty("params", out JsonElement p) &&
                                p.TryGetProperty("data", out JsonElement data))
                            {
                                string? msg = data.GetString();
                                if (!string.IsNullOrEmpty(msg))
                                {
                                    onProgress.Invoke(msg);
                                }
                            }
                        }

                        continue;
                    }

                    if (root.TryGetProperty("error", out JsonElement error))
                    {
                        string messageText = error.TryGetProperty("message", out JsonElement errorMessage)
                            ? errorMessage.GetString() ?? "Unknown MCP error."
                            : "Unknown MCP error.";
                        throw new InvalidOperationException(messageText);
                    }

                    return root.GetProperty("result").Clone();
                }

                throw new OperationCanceledException(cancellationToken);
            }
            finally
            {
                _requestLock.Release();
            }
        }

        private void EnsureRunning()
        {
            if (_process == null || _process.HasExited)
            {
                throw new InvalidOperationException("MCP process is not running.");
            }
        }
    }
}
