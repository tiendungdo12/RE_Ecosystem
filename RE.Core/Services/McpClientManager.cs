using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RE.Core.Abstractions;
using RE.Core.Mcp;

namespace RE.Core.Services
{
    public class McpClientManager : IAsyncDisposable, IDisposable
    {
        private readonly ConcurrentDictionary<string, McpClient> _clients = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _poolLock = new(1, 1);
        private readonly IPlatformEnvironment _platform;
        private McpToolRegistry? _registry;
        private readonly Func<string, string, object, CancellationToken, Task<JsonElement>>? _testInvocation;

        public McpClientManager(IPlatformEnvironment platform)
        {
            _platform = platform;
        }

        internal McpClientManager(IPlatformEnvironment platform,
            Func<string, string, object, CancellationToken, Task<JsonElement>> testInvocation) : this(platform)
        {
            _testInvocation = testInvocation;
        }

        public async Task<McpToolRegistry> GetRegistryAsync()
        {
            if (_registry == null)
            {
                _registry = await McpToolRegistry.LoadAsync();
            }

            return _registry;
        }

        internal async Task<McpClient> GetClientAsync(string toolId, CancellationToken ct = default)
        {
            if (string.Equals(toolId, "static.triage", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    "The legacy 'static.triage' MCP tool has been retired for Phase 1. " +
                    "Deterministic triage must execute via ITriagePipeline without MCP.");
            }

            if (_clients.TryGetValue(toolId, out var existingClient))
            {
                return existingClient;
            }

            await _poolLock.WaitAsync(ct);
            try
            {
                if (_clients.TryGetValue(toolId, out existingClient))
                {
                    return existingClient;
                }

                var registry = await GetRegistryAsync();
                var config = registry.GetRequiredTool(toolId);
                var client = await McpClientFactory.CreateAsync(config, _platform, ct);

                _clients[toolId] = client;
                return client;
            }
            finally
            {
                _poolLock.Release();
            }
        }

        [Obsolete("Legacy parameterless overload targeting static.triage is retired. Use ITriagePipeline for triage or specify toolId for other MCP tools.")]
        public Task<JsonElement> CallToolAsync(string toolName, object arguments)
        {
            throw new NotSupportedException(
                "The legacy 'static.triage' MCP route has been retired for Phase 1. " +
                "Deterministic triage must execute via ITriagePipeline without MCP.");
        }

        public async Task<JsonElement> CallToolAsync(string toolId, string toolName, object arguments,
            CancellationToken ct = default)
        {
            PreH0ToolPolicy.EnsureAllowed(toolId, toolName);
            ct.ThrowIfCancellationRequested();
            if (_testInvocation != null) return await _testInvocation(toolId, toolName, arguments, ct);
            var client = await GetClientAsync(toolId, ct);
            Dictionary<string, JsonElement>? argsDict = null;

            if (arguments is Dictionary<string, JsonElement> dict)
            {
                argsDict = dict;
            }
            else if (arguments != null)
            {
                argsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    JsonSerializer.Serialize(arguments));
            }

            var callResult = await client.CallToolAsync(new CallToolRequestParams
            {
                Name = toolName,
                Arguments = argsDict
            }, ct);

            string jsonOutput = "{}";
            if (callResult.Content != null && callResult.Content.Count > 0)
            {
                var textBlock = callResult.Content.FirstOrDefault(c => c is TextContentBlock) as TextContentBlock;
                if (textBlock != null)
                {
                    jsonOutput = textBlock.Text;
                }
                else
                {
                    jsonOutput = callResult.Content[0].ToString() ?? "{}";
                }
            }

            try
            {
                return JsonSerializer.Deserialize<JsonElement>(jsonOutput);
            }
            catch
            {
                // Fallback nếu kết quả là text thô (không phải json)
                var fallbackObj = new Dictionary<string, object?>
                {
                    ["raw_text"] = jsonOutput,
                    ["is_error"] = callResult.IsError ?? false
                };
                return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(fallbackObj));
            }
        }

        public async Task<McpConnectionStatus> CheckStatusAsync(string toolId, CancellationToken ct = default)
        {
            var registry = await GetRegistryAsync();
            var config =
                registry.Tools.FirstOrDefault(t => string.Equals(t.Id, toolId, StringComparison.OrdinalIgnoreCase));
            if (config == null)
            {
                return new McpConnectionStatus
                {
                    ToolId = toolId,
                    Name = toolId,
                    IsConnected = false,
                    ToolCount = 0,
                    Error = $"Tool '{toolId}' is not registered."
                };
            }

            try
            {
                var client = await GetClientAsync(toolId, ct);
                var toolsResult = await client.ListToolsAsync(new ListToolsRequestParams(), ct);
                int count = toolsResult.Tools?.Count ?? 0;

                return new McpConnectionStatus
                {
                    ToolId = toolId,
                    Name = config.Name,
                    Transport = config.Transport,
                    Endpoint = config.Endpoint,
                    IsConnected = true,
                    ToolCount = count,
                    Error = null
                };
            }
            catch (Exception ex)
            {
                // Remove failed client from pool so subsequent attempts recreate connection
                await DisconnectAsync(toolId);

                return new McpConnectionStatus
                {
                    ToolId = toolId,
                    Name = config.Name,
                    Transport = config.Transport,
                    Endpoint = config.Endpoint,
                    IsConnected = false,
                    ToolCount = 0,
                    Error = ex.Message
                };
            }
        }

        public async Task<List<McpConnectionStatus>> CheckAllStatusAsync(CancellationToken ct = default)
        {
            var registry = await GetRegistryAsync();
            var results = new List<McpConnectionStatus>();

            foreach (var tool in registry.Tools.Where(t => t.Enabled))
            {
                results.Add(await CheckStatusAsync(tool.Id, ct));
            }

            return results;
        }

        public async Task DisconnectAsync(string toolId)
        {
            if (_clients.TryRemove(toolId, out var client))
            {
                try
                {
                    await client.DisposeAsync();
                }
                catch
                {
                    // Ignore dispose errors on disconnected/errored clients
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in _clients.Values)
            {
                try
                {
                    await client.DisposeAsync();
                }
                catch
                {
                    // Ignore dispose errors
                }
            }

            _clients.Clear();
            _poolLock.Dispose();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
