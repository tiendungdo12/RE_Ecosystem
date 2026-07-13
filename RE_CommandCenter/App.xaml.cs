using Microsoft.UI.Xaml;

namespace RE_CommandCenter;

public partial class App : Application
{
    public Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Load Global Settings
        await Core.Settings.SettingsService.LoadAsync();

        // Diem vao cua app WinUI.
        // Giai doan 1 chi can mo MainWindow, moi luong chat/Ollama nam ben trong ChatPage/ViewModel.
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
