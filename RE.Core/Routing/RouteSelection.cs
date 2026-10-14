namespace RE.Core.Routing
{
    public sealed class RouteSelection
    {
        public RouteInput Input { get; init; } = new RouteInput();

        public RouteRule? Rule { get; init; }

        public bool Matched => Rule != null;
    }
}