using System.IO;
using System.Text.Json;

namespace RE.Core.Routing
{
    public sealed class RouteInput
    {
        public string Kind { get; init; } = "file";

        public string Path { get; init; } = string.Empty;

        public bool Exists { get; init; }

        public string Extension { get; init; } = string.Empty;

        public string FileType { get; init; } = string.Empty;

        public string Platform { get; init; } = string.Empty;

        public string Architecture { get; init; } = string.Empty;

        public string Packer { get; init; } = string.Empty;

        public string Engine { get; init; } = string.Empty;

        public string RecommendedPipeline { get; init; } = string.Empty;

        // Tao facts toi thieu tu duong dan nguoi dung dua vao.
        // Day la fallback khi chua co triage JSON.
        public static RouteInput FromPath(string path)
        {
            string normalizedPath = System.IO.Path.GetFullPath(path.Trim().Trim('"'));
            return new RouteInput
            {
                Kind = File.Exists(normalizedPath) ? "file" : "unknown",
                Path = normalizedPath,
                Exists = File.Exists(normalizedPath),
                Extension = System.IO.Path.GetExtension(normalizedPath).ToLowerInvariant()
            };
        }

        // Tao facts tu output cua static.triage.
        // Day la dau vao chuan cua Branch Table tu Block 3.2 tro di.
        public static RouteInput FromTriage(JsonElement triage)
        {
            string path = ReadString(triage, "path");
            string normalizedPath = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : System.IO.Path.GetFullPath(path);

            return new RouteInput
            {
                Kind = ReadBool(triage, "is_file") ? "file" : "unknown",
                Path = normalizedPath,
                Exists = ReadBool(triage, "exists"),
                Extension = string.IsNullOrWhiteSpace(normalizedPath)
                    ? string.Empty
                    : System.IO.Path.GetExtension(normalizedPath).ToLowerInvariant(),
                FileType = ReadString(triage, "file_type"),
                Platform = ReadString(triage, "platform"),
                Architecture = ReadString(triage, "architecture"),
                Packer = ReadString(triage, "packer"),
                Engine = ReadString(triage, "engine"),
                RecommendedPipeline = ReadString(triage, "recommended_pipeline")
            };
        }

        private static string ReadString(JsonElement source, string propertyName)
        {
            return source.TryGetProperty(propertyName, out JsonElement property) &&
                   property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool ReadBool(JsonElement source, string propertyName)
        {
            return source.TryGetProperty(propertyName, out JsonElement property) &&
                   property.ValueKind == JsonValueKind.True;
        }
    }
}