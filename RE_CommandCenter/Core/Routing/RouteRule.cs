using System;
using System.Collections.Generic;
using System.Linq;

namespace RE_CommandCenter.Core.Routing
{
    public sealed class RouteRule
    {
        public string Id { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public int Priority { get; set; }

        public string MatchKind { get; set; } = string.Empty;

        public bool RequireExists { get; set; }

        public List<string> MatchExtensions { get; } = new List<string>();

        public string MatchFileType { get; set; } = string.Empty;

        public string MatchPlatform { get; set; } = string.Empty;

        public string MatchEngine { get; set; } = string.Empty;

        public string MatchPacker { get; set; } = string.Empty;

        public string MatchRecommendedPipeline { get; set; } = string.Empty;

        public bool Planned { get; set; }

        public RouteAction Action { get; } = new RouteAction();

        // Kiem tra facts co khop rule khong.
        // Router chi dung cac rule enabled, nhung Matches van giu dieu kien rieng de de test/doc.
        public bool Matches(RouteInput input)
        {
            if (!string.IsNullOrWhiteSpace(MatchKind) &&
                !string.Equals(MatchKind, input.Kind, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (RequireExists && !input.Exists)
            {
                return false;
            }

            if (MatchExtensions.Count > 0 &&
                !MatchExtensions.Any(extension => string.Equals(extension, input.Extension, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (!MatchesText(MatchFileType, input.FileType))
            {
                return false;
            }

            if (!MatchesText(MatchPlatform, input.Platform))
            {
                return false;
            }

            if (!MatchesText(MatchEngine, input.Engine))
            {
                return false;
            }

            if (!MatchesText(MatchPacker, input.Packer))
            {
                return false;
            }

            if (!MatchesText(MatchRecommendedPipeline, input.RecommendedPipeline))
            {
                return false;
            }

            return true;
        }

        private static bool MatchesText(string expected, string actual)
        {
            return string.IsNullOrWhiteSpace(expected) ||
                   string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }
    }
}
