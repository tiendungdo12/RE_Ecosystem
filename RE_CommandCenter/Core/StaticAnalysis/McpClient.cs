using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RE_CommandCenter.Core.StaticAnalysis
{
    public class McpClient : IDisposable
    {
        private Process? _process;
        private StreamWriter? _stdin;
        private StreamReader? _stdout;
        private StreamReader? _stderr;
        private int _nextId = 1;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode>> _pendingRequests = new();
        private CancellationTokenSource? _cts;
        private Task? _readLoopTask;
        private bool _isInitialized = false;

        public async Task EnsureStartedAsync(string pythonExe, string serverScriptPath)
        {
            if (_isInitialized && _process != null && !_process.HasExited)
            {
                return; // Already running
            }

            // Cleanup previous run if any
            Dispose();

            _cts = new CancellationTokenSource();

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{serverScriptPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(serverScriptPath),
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };

            _process = Process.Start(psi);
            if (_process == null) throw new Exception("Failed to start Python MCP Server.");

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;
            _stderr = _process.StandardError;
            
            // Start the reading loop
            _readLoopTask = Task.Run(() => ReadLoopAsync(_cts.Token));

            // Send initialize request
            var initParams = new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "RE_CommandCenter",
                    ["version"] = "1.0.0"
                }
            };

            var initResponse = await SendRequestAsync("initialize", initParams);
            
            // Send initialized notification
            SendNotification("notifications/initialized", new JsonObject());

            _isInitialized = true;
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            if (_stdout == null) return;
            
            try
            {
                while (!token.IsCancellationRequested && !_stdout.EndOfStream)
                {
                    string? line = await _stdout.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var root = JsonNode.Parse(line) as JsonObject;
                        if (root == null) continue;

                        if (root.ContainsKey("id") && root["id"] != null)
                        {
                            int id = root["id"]!.GetValue<int>();
                            if (_pendingRequests.TryRemove(id, out var tcs))
                            {
                                if (root.ContainsKey("error") && root["error"] != null)
                                {
                                    tcs.TrySetException(new Exception(root["error"]!.ToJsonString()));
                                }
                                else if (root.ContainsKey("result"))
                                {
                                    tcs.TrySetResult(root["result"]!);
                                }
                                else
                                {
                                    tcs.TrySetResult(new JsonObject());
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error parsing MCP message: {ex.Message} - Line: {line}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MCP Read Loop stopped: {ex.Message}");
            }
        }

        public async Task<JsonNode> CallToolAsync(string toolName, JsonObject arguments)
        {
            var p = new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments
            };

            var result = await SendRequestAsync("tools/call", p);
            
            // FastMCP typically returns tool results in content[0].text
            if (result?["content"] is JsonArray contentArray && contentArray.Count > 0)
            {
                var textNode = contentArray[0]?["text"];
                if (textNode != null)
                {
                    string text = textNode.GetValue<string>();
                    try
                    {
                        // Try to parse the text as JSON, because FastMCP serializes dicts to text
                        return JsonNode.Parse(text)!;
                    }
                    catch
                    {
                        // If it's just a raw string
                        return new JsonObject { ["result_text"] = text };
                    }
                }
            }
            
            if (result?["isError"]?.GetValue<bool>() == true)
            {
                throw new Exception("Tool execution returned an error.");
            }

            return result ?? new JsonObject();
        }

        private Task<JsonNode> SendRequestAsync(string method, JsonObject parameters)
        {
            if (_stdin == null) throw new InvalidOperationException("MCP Client is not connected.");

            int id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<JsonNode>();
            _pendingRequests[id] = tcs;

            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters
            };

            string json = request.ToJsonString();
            _stdin.WriteLine(json);
            _stdin.Flush();

            return tcs.Task;
        }

        private void SendNotification(string method, JsonObject parameters)
        {
            if (_stdin == null) return;

            var notification = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters
            };

            string json = notification.ToJsonString();
            _stdin.WriteLine(json);
            _stdin.Flush();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _isInitialized = false;

            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill();
                }
                catch { }
            }

            _stdin?.Dispose();
            _stdout?.Dispose();
            _stderr?.Dispose();
            _process?.Dispose();
        }
    }
}
