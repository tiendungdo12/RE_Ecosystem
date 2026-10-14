using RE.Application.Models;

namespace RE.Application.UseCases;

public sealed class ModelSelectionService
{
    public SelectableModel? FindSavedModel(
        IEnumerable<SelectableModel> models,
        string? provider,
        string? modelName)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(modelName))
            return null;

        return models.FirstOrDefault(model =>
            model.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
            model.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));
    }
}