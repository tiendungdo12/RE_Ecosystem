using System.Collections.Generic;

namespace RE_CommandCenter.Core.Mcp
{
    public sealed class McpToolConfig
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Cluster { get; set; } = string.Empty;

        public string Transport { get; set; } = "stdio";

        public string Runtime { get; set; } = string.Empty;

        public string WorkingDirectory { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public List<string> Args { get; } = new List<string>();

        public bool Enabled { get; set; } = true;
    }
}
