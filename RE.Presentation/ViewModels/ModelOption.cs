using System.Collections.Generic;

namespace RE.Presentation.ViewModels;

public sealed class ModelOption
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string MenuName { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public bool IsCloud { get; init; }
    public string ScopeLabel { get; init; } = string.Empty;
    public string SpeedBadge { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string ModelEnum { get; init; } = string.Empty;
    public string CurrentSelectionBadge { get; set; } = string.Empty;
    public List<ModelOption> SubModels { get; } = new();
    public string Details => string.IsNullOrWhiteSpace(ScopeLabel) ? Provider : $"{Provider} · {ScopeLabel}";

    public string ProviderIcon =>
        Provider.Equals("Antigravity", StringComparison.OrdinalIgnoreCase) ? "\uE9F3" : "\uE943";

    public string ProviderColor => Provider.Equals("Antigravity", StringComparison.OrdinalIgnoreCase)
        ? "SystemAccentColor"
        : "SystemFillColorSuccessBrush";

    public override string ToString() => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : Name;
}