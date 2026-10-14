using System.Collections.ObjectModel;

namespace RE.Presentation.ViewModels;

public sealed class ModelProviderGroup
{
    public string Provider { get; init; } = string.Empty;
    public ObservableCollection<ModelOption> Models { get; } = new();
}