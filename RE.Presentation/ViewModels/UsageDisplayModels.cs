using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RE.Presentation.ViewModels;

public partial class UsageBucketDisplayModel : ObservableObject
{
    [ObservableProperty] public partial string DisplayName { get; set; } = string.Empty;
    [ObservableProperty] public partial string RemainingPercentText { get; set; } = string.Empty;
    [ObservableProperty] public partial double RemainingPercent { get; set; }
    [ObservableProperty] public partial string ResetTimeText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasResetTime { get; set; }
    [ObservableProperty] public partial bool IsInformationOnly { get; set; }
}

public partial class UsageGroupDisplayModel : ObservableObject
{
    [ObservableProperty] public partial string DisplayName { get; set; } = string.Empty;
    public ObservableCollection<UsageBucketDisplayModel> Buckets { get; } = new();
}