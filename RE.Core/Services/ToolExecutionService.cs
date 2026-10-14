using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RE.Core.Mcp;

namespace RE.Core.Services
{
    public class ToolExecutionService
    {
        public async Task<JsonElement> ExecuteToolAsync(string toolId, string toolName, string targetPath)
        {
            PreH0ToolPolicy.EnsureAllowed(toolId, toolName);
            McpToolRegistry registry = await McpToolRegistry.LoadAsync();
            McpToolConfig toolConfig = registry.GetRequiredTool(toolId);
            await using var client = await McpClientFactory.CreateAsync(toolConfig);

            var argsDict =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    JsonSerializer.Serialize(new { path = targetPath }));
            var callResult = await client.CallToolAsync(new CallToolRequestParams
                { Name = toolName, Arguments = argsDict });

            string resultJson = "{}";
            if (callResult.Content != null && callResult.Content.Count > 0)
            {
                var textBlock = callResult.Content.FirstOrDefault(c => c is TextContentBlock) as TextContentBlock;
                resultJson = textBlock != null ? textBlock.Text : callResult.Content[0].ToString() ?? "{}";
            }

            return JsonSerializer.Deserialize<JsonElement>(resultJson);
        }
    }
}
