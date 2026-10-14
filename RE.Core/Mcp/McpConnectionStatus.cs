using System;

namespace RE.Core.Mcp
{
    public sealed record McpConnectionStatus
    {
        public string ToolId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Transport { get; init; } = string.Empty;
        public string Endpoint { get; init; } = string.Empty;
        public bool IsConnected { get; init; }
        public int ToolCount { get; init; }
        public string? Error { get; init; }
    }
}