using CommunityToolkit.Mvvm.ComponentModel;

namespace RE.WinUI.ViewModels
{
    public partial class ChatMessage : ObservableObject
    {
        [ObservableProperty] public partial string Role { get; set; } = string.Empty;

        [ObservableProperty] public partial string Content { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsThinking { get; set; }
    }
}