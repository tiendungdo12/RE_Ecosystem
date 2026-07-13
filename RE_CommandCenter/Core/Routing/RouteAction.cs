namespace RE_CommandCenter.Core.Routing
{
    public sealed class RouteAction
    {
        public string Pipeline { get; set; } = string.Empty;

        public string ToolId { get; set; } = string.Empty;

        public string ToolName { get; set; } = string.Empty;

        public string ArgumentPath { get; set; } = "$input.path";
    }
}
