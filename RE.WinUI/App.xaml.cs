using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RE.Application;
using RE.Core.Abstractions;
using RE.Core.Services;
using RE.Infrastructure.AI;
using RE.Infrastructure.Persistence;
using RE.Infrastructure;
using ApplicationUseCases = RE.Application.UseCases;
using RE.Platform.Windows;
using RE.WinUI.ViewModels;

namespace RE.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
    public Window? MainWindow { get; private set; }
    public IServiceProvider Services { get; }

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Platform specific
        services.AddSingleton<IPlatformEnvironment, WindowsEnvironment>();
        services.AddSingleton<ISignatureVerifier, AuthenticodeVerifier>();
        services.AddSingleton<IFilePicker, WinFilePicker>();

        // Shared application/infrastructure composition
        services.AddApplication();
        services.AddInfrastructure();

        // ViewModels
        services.AddTransient<TriageViewModel>();
        services.AddTransient<ChatViewModel>();

        return services.BuildServiceProvider().UseTriageOrchestrator();
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Load Global Settings
        await Core.Settings.SettingsService.LoadAsync();

        // Diem vao cua app WinUI.
        // Giai doan 1 chi can mo MainWindow, moi luong chat/Ollama nam ben trong ChatPage/ViewModel.
        MainWindow = new MainWindow();
        WinFilePicker.WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
        MainWindow.Closed += (s, e) => { AntigravityServerManager.Instance.Stop(); };
        MainWindow.Activate();
        Core.Settings.SettingsService.SettingsError += (_, _) =>
            MainWindow.DispatcherQueue.TryEnqueue(async () => await ShowSettingsErrorAsync());
        if (!string.IsNullOrEmpty(Core.Settings.SettingsService.LastError)) await ShowSettingsErrorAsync();
    }

    private Task ShowSettingsErrorAsync()
    {
        if (MainWindow is RE.WinUI.MainWindow shell) shell.ShowSettingsError(Core.Settings.SettingsService.LastError);
        return Task.CompletedTask;
    }
}
