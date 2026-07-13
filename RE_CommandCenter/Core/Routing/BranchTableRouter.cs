using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RE_CommandCenter.Core.Routing
{
    public sealed class BranchTableRouter
    {
        public List<RouteRule> Routes { get; } = new List<RouteRule>();

        // Doc ROUTING_RULES.yaml: day la lop dieu phoi deterministic.
        // AI chi nen vao cuoc khi khong co route nao match hoac route/tool that bai.
        public static async Task<BranchTableRouter> LoadAsync()
        {
            string routePath = Path.Combine(AgentConfigLoader.GetConfigFolderPath(), "ROUTING_RULES.yaml");
            string yaml = await File.ReadAllTextAsync(routePath);
            return Parse(yaml);
        }

        // Chon rule uu tien cao nhat khop voi input facts.
        // Tu Block 3.2, input facts den tu static.triage thay vi chi tu extension/path.
        public RouteSelection Select(RouteInput input)
        {
            RouteRule? selected = Routes
                .Where(route => route.Matches(input))
                .OrderByDescending(route => route.Priority)
                .FirstOrDefault();

            return new RouteSelection
            {
                Input = input,
                Rule = selected
            };
        }

        private static BranchTableRouter Parse(string yaml)
        {
            var router = new BranchTableRouter();
            RouteRule? current = null;

            foreach (string rawLine in yaml.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("- id:", StringComparison.Ordinal))
                {
                    current = new RouteRule { Id = ReadValue(line.Substring(2)) };
                    router.Routes.Add(current);
                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                if (line.StartsWith("enabled:", StringComparison.Ordinal))
                {
                    current.Enabled = bool.TryParse(ReadValue(line), out bool enabled) && enabled;
                }
                else if (line.StartsWith("planned:", StringComparison.Ordinal))
                {
                    current.Planned = bool.TryParse(ReadValue(line), out bool planned) && planned;
                }
                else if (line.StartsWith("priority:", StringComparison.Ordinal))
                {
                    current.Priority = int.TryParse(ReadValue(line), out int priority) ? priority : 0;
                }
                else if (line.StartsWith("match_kind:", StringComparison.Ordinal))
                {
                    current.MatchKind = ReadValue(line);
                }
                else if (line.StartsWith("require_exists:", StringComparison.Ordinal))
                {
                    current.RequireExists = bool.TryParse(ReadValue(line), out bool requireExists) && requireExists;
                }
                else if (line.StartsWith("match_extensions:", StringComparison.Ordinal))
                {
                    foreach (string extension in ReadValue(line).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        current.MatchExtensions.Add(extension.ToLowerInvariant());
                    }
                }
                else if (line.StartsWith("match_file_type:", StringComparison.Ordinal))
                {
                    current.MatchFileType = ReadValue(line);
                }
                else if (line.StartsWith("match_platform:", StringComparison.Ordinal))
                {
                    current.MatchPlatform = ReadValue(line);
                }
                else if (line.StartsWith("match_engine:", StringComparison.Ordinal))
                {
                    current.MatchEngine = ReadValue(line);
                }
                else if (line.StartsWith("match_packer:", StringComparison.Ordinal))
                {
                    current.MatchPacker = ReadValue(line);
                }
                else if (line.StartsWith("match_recommended_pipeline:", StringComparison.Ordinal))
                {
                    current.MatchRecommendedPipeline = ReadValue(line);
                }
                else if (line.StartsWith("pipeline:", StringComparison.Ordinal))
                {
                    current.Action.Pipeline = ReadValue(line);
                }
                else if (line.StartsWith("tool_id:", StringComparison.Ordinal))
                {
                    current.Action.ToolId = ReadValue(line);
                }
                else if (line.StartsWith("tool_name:", StringComparison.Ordinal))
                {
                    current.Action.ToolName = ReadValue(line);
                }
                else if (line.StartsWith("argument_path:", StringComparison.Ordinal))
                {
                    current.Action.ArgumentPath = ReadValue(line);
                }
            }

            return router;
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
