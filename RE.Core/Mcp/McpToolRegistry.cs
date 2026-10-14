using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace RE.Core.Mcp
{
    public sealed class McpToolRegistry
    {
        public string ProtocolVersion { get; private set; } = "2025-11-25";

        public List<McpToolConfig> Tools { get; } = new List<McpToolConfig>();

        // Doc registry MCP tu AgentConfigs. File YAML nay la "ban ve noi day":
        // app khong can hard-code tool nam dau hay chay bang lenh gi.
        public static async Task<McpToolRegistry> LoadAsync()
        {
            string registryPath = Path.Combine(AgentConfigLoader.GetConfigFolderPath(), "MCP_TOOLS.yaml");
            string yaml = await File.ReadAllTextAsync(registryPath);
            return Parse(yaml);
        }

        // Lay tool theo id logic neu co va enabled, nguoc lai tra ve null.
        public McpToolConfig? GetTool(string id)
        {
            return Tools.Find(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase) && item.Enabled);
        }

        // Lay tool theo id logic, vi du "static.md5".
        // Neu tool bi thieu hoac dang disabled thi dung som de tranh goi nham tool.
        public McpToolConfig GetRequiredTool(string id)
        {
            McpToolConfig? tool = Tools.Find(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (tool == null)
            {
                throw new InvalidOperationException($"MCP tool is not registered: {id}");
            }

            if (!tool.Enabled)
            {
                throw new InvalidOperationException($"MCP tool is disabled: {id}");
            }

            return tool;
        }

        // Parser YAML toi thieu cho schema MCP_TOOLS.yaml hien tai.
        // Khi registry lon hon, ta co the thay bang YamlDotNet ma khong doi API ben ngoai.
        private static McpToolRegistry Parse(string yaml)
        {
            var registry = new McpToolRegistry();
            McpToolConfig? current = null;
            bool readingArgs = false;

            foreach (string rawLine in yaml.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("protocol_version:", StringComparison.Ordinal))
                {
                    registry.ProtocolVersion = ReadValue(line);
                    continue;
                }

                if (line.StartsWith("- id:", StringComparison.Ordinal))
                {
                    current = new McpToolConfig { Id = ReadValue(line.Substring(2)) };
                    registry.Tools.Add(current);
                    readingArgs = false;
                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                if (line.StartsWith("args:", StringComparison.Ordinal))
                {
                    readingArgs = true;
                    continue;
                }

                if (readingArgs && line.StartsWith("-", StringComparison.Ordinal))
                {
                    current.Args.Add(Unquote(line.Substring(1).Trim()));
                    continue;
                }

                readingArgs = false;

                if (line.StartsWith("name:", StringComparison.Ordinal))
                {
                    current.Name = ReadValue(line);
                }
                else if (line.StartsWith("cluster:", StringComparison.Ordinal))
                {
                    current.Cluster = ReadValue(line);
                }
                else if (line.StartsWith("transport:", StringComparison.Ordinal))
                {
                    current.Transport = ReadValue(line);
                }
                else if (line.StartsWith("endpoint:", StringComparison.Ordinal))
                {
                    current.Endpoint = ReadValue(line);
                }
                else if (line.StartsWith("transport_mode:", StringComparison.Ordinal))
                {
                    current.TransportMode = ReadValue(line);
                }
                else if (line.StartsWith("timeout_seconds:", StringComparison.Ordinal))
                {
                    if (int.TryParse(ReadValue(line), out int timeoutSeconds))
                    {
                        current.TimeoutSeconds = timeoutSeconds;
                    }
                }
                else if (line.StartsWith("runtime:", StringComparison.Ordinal))
                {
                    current.Runtime = ReadValue(line);
                }
                else if (line.StartsWith("working_directory:", StringComparison.Ordinal))
                {
                    current.WorkingDirectory = ReadValue(line);
                }
                else if (line.StartsWith("command:", StringComparison.Ordinal))
                {
                    current.Command = ReadValue(line);
                }
                else if (line.StartsWith("enabled:", StringComparison.Ordinal))
                {
                    current.Enabled = bool.TryParse(ReadValue(line), out bool enabled) && enabled;
                }
            }

            return registry;
        }

        private static string ReadValue(string line)
        {
            int separator = line.IndexOf(':');
            if (separator < 0)
            {
                return string.Empty;
            }

            return Unquote(line.Substring(separator + 1).Trim());
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') ||
                 (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }
    }
}