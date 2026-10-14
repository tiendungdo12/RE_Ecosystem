using System.Text.Json;
using System.Threading.Tasks;
using RE.Domain.Models;
using RE.Core.Routing;
// using RE.Core.Workspace;
// using RE.Core.Persistence;
using System;

namespace RE.Core.Services
{
    public class RoutingService
    {
        private readonly TriageService _triageService;
        private readonly ToolExecutionService _toolExecutionService;

        public RoutingService(TriageService triageService, ToolExecutionService toolExecutionService)
        {
            _triageService = triageService;
            _toolExecutionService = toolExecutionService;
        }

        public async Task<string> RouteAndExecuteAsync(string filePath)
        {
            // Placeholder implementation
            return await Task.FromResult($"RouteAndExecuteAsync placeholder for {filePath}");
        }

        private async Task<string> HandleAiFallbackAsync(string originalPath, string stagedPath, JsonElement triage,
            RouteInput input, string reason)
        {
            return $"AI Fallback triggered ({reason}) for {originalPath}";
        }

        private string FormatToolResult(JsonElement result)
        {
            JsonElement structured = result.TryGetProperty("structuredContent", out JsonElement structuredContent)
                ? structuredContent
                : result;

            if (structured.TryGetProperty("path", out JsonElement resultPath) &&
                structured.TryGetProperty("md5", out JsonElement md5) &&
                structured.TryGetProperty("size", out JsonElement size))
            {
                return $"MD5: {md5.GetString()}\nSize: {size.GetInt64()} bytes\nPath: {resultPath.GetString()}";
            }

            return JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}