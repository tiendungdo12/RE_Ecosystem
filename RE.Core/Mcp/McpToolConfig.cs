using System.Collections.Generic;

namespace RE.Core.Mcp
{
    public sealed class McpToolConfig
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Cluster { get; set; } = string.Empty;

        public string Transport { get; set; } = "stdio";

        /// <summary>
        /// URL endpoint. Chỉ dùng khi Transport = "http". Ví dụ: "http://127.0.0.1:3000/mcp"
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Chế độ transport: "auto" (mặc định) | "sse" | "streamable_http"
        /// </summary>
        public string TransportMode { get; set; } = "auto";

        /// <summary>
        /// Timeout cho mỗi lần gọi tool (giây). Mặc định 30s.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        public string Runtime { get; set; } = string.Empty;

        public string WorkingDirectory { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public List<string> Args { get; } = new List<string>();

        public bool Enabled { get; set; } = true;
    }
}