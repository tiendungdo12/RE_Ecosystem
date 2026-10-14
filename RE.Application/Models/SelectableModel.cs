namespace RE.Application.Models;

/// <summary>
/// UI-neutral model identity used by model selection use-cases.
/// </summary>
public sealed record SelectableModel(string Name, string Provider);