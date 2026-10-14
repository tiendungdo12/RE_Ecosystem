using Microsoft.UI.Xaml;
using RE.Infrastructure.AI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RE.WinUI.Core.Settings;
using RE.WinUI.Views;
using System;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace RE.WinUI;

public sealed partial class MainWindow : Window
{
    public void ShowSettingsError(string message)
    {
        SettingsStorageError.Message = message;
        SettingsStorageError.IsOpen = !string.IsNullOrEmpty(message);
    }
    private readonly UISettings _uiSettings = new();
    private string? _lastAppliedBackdropMaterial;
    private Color? _lastAppliedAccentColor;
    private int _accentRefreshVersion;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Title = "RE CommandCenter 2.0";

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Keep the responsive layout usable when the user resizes the window.
        // The usage flyout is designed for this minimum content width.
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 900;
            presenter.PreferredMinimumHeight = 620;
        }

        // Listen to settings changes to apply backdrop/theme dynamically
        SettingsService.SettingsChanged += SettingsService_SettingsChanged;
        _uiSettings.ColorValuesChanged += UISettings_ColorValuesChanged;
        Closed += MainWindow_Closed;
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigated += ContentFrame_Navigated;
        NavView.SelectedItem = NavChat;
        ApplyCurrentSettings();
        UpdateBackEnabled();
    }

    private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        UpdateBackEnabled();
    }

    public void UpdateBackEnabled()
    {
        bool isSettingsDetail = ContentFrame.Content is SettingsPage sp && sp.IsShowingDetail;
        NavView.IsBackEnabled = ContentFrame.CanGoBack || isSettingsDetail;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            if (ContentFrame.Content is SettingsPage sp)
            {
                sp.NavigateToMain();
            }
            else
            {
                ContentFrame.Navigate(typeof(SettingsPage));
            }
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            if (item.Tag?.ToString() == "ChatPage")
            {
                ContentFrame.Navigate(typeof(ChatPage));
            }
            else if (item.Tag?.ToString() == "TriagePage")
            {
                ContentFrame.Navigate(typeof(TriagePage));
            }
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            if (ContentFrame.Content is SettingsPage sp)
            {
                sp.NavigateToMain();
            }
            else
            {
                ContentFrame.Navigate(typeof(SettingsPage));
            }
        }
    }

    private void SettingsService_SettingsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyCurrentSettings);
    }

    private async void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.Content is SettingsPage sp && sp.IsShowingDetail)
        {
            await sp.TryGoBackAsync();
            UpdateBackEnabled();
            return;
        }

        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();

            // Sync NavView selection
            if (ContentFrame.Content is ChatPage)
                NavView.SelectedItem = NavChat;
            else if (ContentFrame.Content is TriagePage)
                NavView.SelectedItem = NavTriage;
            else if (ContentFrame.Content is SettingsPage)
                NavView.SelectedItem = NavView.SettingsItem;

            UpdateBackEnabled();
        }
    }

    private void UISettings_ColorValuesChanged(UISettings sender, object args)
    {
        if (SettingsService.Current.UseSystemAccentColor)
        {
            DispatcherQueue.TryEnqueue(ApplyCurrentSettings);
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SettingsService.SettingsChanged -= SettingsService_SettingsChanged;
        _uiSettings.ColorValuesChanged -= UISettings_ColorValuesChanged;
        ContentFrame.Navigated -= ContentFrame_Navigated;
    }

    private void ApplyCurrentSettings()
    {
        var settings = SettingsService.Current;
        var targetColor = ResolveAccentColor(settings);
        ApplyAccentColor(targetColor);

        // 1. Apply Theme
        ApplyTheme(settings.AppTheme);

        // 2. Apply Backdrop
        if (_lastAppliedBackdropMaterial != settings.BackdropMaterial)
        {
            SystemBackdrop = settings.BackdropMaterial switch
            {
                "Mica" => new MicaBackdrop() { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
                "MicaAlt" => new MicaBackdrop() { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
                "Acrylic" => new DesktopAcrylicBackdrop(),
                _ => null
            };
            _lastAppliedBackdropMaterial = settings.BackdropMaterial;
        }
    }

    public void UpdateLanguage()
    {
        try
        {
            var loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            AppTitleTextBlock.Text = loader.GetString("AppTitleTextBlock/Text");
            NavChat.Content = loader.GetString("NavChat/Content");
            NavTriage.Content = loader.GetString("NavTriage/Content");
            if (NavView.SettingsItem is NavigationViewItem settingsItem)
            {
                settingsItem.Content = loader.GetString("NavSettings/Content");
            }
        }
        catch
        {
        }
    }

    /// <summary>
    public void ForceReloadPages()
    {
        UpdateLanguage();

        // The current page remains mounted when the language changes, so its
        // runtime-updated controls must be refreshed explicitly.
        if (ContentFrame.Content is ChatPage chatPage)
        {
            chatPage.UpdateLanguage();
        }
        else if (ContentFrame.Content is SettingsPage settingsPage)
        {
            settingsPage.UpdateLanguage();
        }

        // Clear back stack so WinUI won't reuse cached page instances
        ContentFrame.BackStack.Clear();
    }

    private void ApplyTheme(ElementTheme requestedTheme)
    {
        if (Content is not FrameworkElement rootElement || rootElement.RequestedTheme == requestedTheme)
        {
            return;
        }

        var currentEffectiveTheme = rootElement.ActualTheme;
        var targetEffectiveTheme = requestedTheme == ElementTheme.Default
            ? GetCurrentSystemTheme()
            : requestedTheme;

        if (currentEffectiveTheme == targetEffectiveTheme)
        {
            return;
        }

        rootElement.RequestedTheme = requestedTheme;
    }

    private Color ResolveAccentColor(GlobalSettings settings)
    {
        if (settings.UseSystemAccentColor)
        {
            return _uiSettings.GetColorValue(UIColorType.Accent);
        }

        try
        {
            var hex = settings.CustomAccentColor.Replace("#", "");
            byte a = hex.Length == 8 ? Convert.ToByte(hex.Substring(0, 2), 16) : (byte)255;
            int rIndex = hex.Length == 8 ? 2 : 0;
            byte r = Convert.ToByte(hex.Substring(rIndex, 2), 16);
            byte g = Convert.ToByte(hex.Substring(rIndex + 2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(rIndex + 4, 2), 16);
            return Color.FromArgb(a, r, g, b);
        }
        catch
        {
            return _uiSettings.GetColorValue(UIColorType.Accent);
        }
    }

    private void ApplyAccentColor(Color targetColor)
    {
        if (_lastAppliedAccentColor == targetColor)
        {
            return;
        }

        _lastAppliedAccentColor = targetColor;
        var accent = Color.FromArgb(255, targetColor.R, targetColor.G, targetColor.B);
        var accentLight1 = Blend(accent, White, 0.25);
        var accentLight2 = Blend(accent, White, 0.45);
        var accentLight3 = Blend(accent, White, 0.65);
        var accentDark1 = Blend(accent, Black, 0.20);
        var accentDark2 = Blend(accent, Black, 0.35);
        var accentDark3 = Blend(accent, Black, 0.50);
        var textOnAccent = GetReadableTextColor(accent);

        try
        {
            var res = Microsoft.UI.Xaml.Application.Current.Resources;

            foreach (var themeDictionary in res.ThemeDictionaries.Values)
            {
                if (themeDictionary is ResourceDictionary themeResourceDictionary)
                {
                    ApplyPaletteAccent(themeResourceDictionary, accent);
                    ApplyAccentResources(themeResourceDictionary, accent, accentLight1, accentLight2, accentLight3,
                        accentDark1, accentDark2, accentDark3, textOnAccent);
                }
            }

            ApplyAccentResources(res, accent, accentLight1, accentLight2, accentLight3, accentDark1, accentDark2,
                accentDark3, textOnAccent);
            QueueAccentVisualRefresh();
        }
        catch
        {
        }
    }

    private static void ApplyPaletteAccent(ResourceDictionary resources, Color accent)
    {
        foreach (var value in resources.Values)
        {
            if (value is Microsoft.UI.Xaml.ColorPaletteResources palette)
            {
                palette.Accent = accent;
            }
        }
    }

    private static void ApplyAccentResources(
        ResourceDictionary resources,
        Color accent,
        Color accentLight1,
        Color accentLight2,
        Color accentLight3,
        Color accentDark1,
        Color accentDark2,
        Color accentDark3,
        Color textOnAccent)
    {
        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorLight1"] = accentLight1;
        resources["SystemAccentColorLight2"] = accentLight2;
        resources["SystemAccentColorLight3"] = accentLight3;
        resources["SystemAccentColorDark1"] = accentDark1;
        resources["SystemAccentColorDark2"] = accentDark2;
        resources["SystemAccentColorDark3"] = accentDark3;

        SetBrush(resources, "SystemAccentColorBrush", accent);
        SetBrush(resources, "SystemControlForegroundAccentBrush", accent);
        SetBrush(resources, "SystemControlHighlightAccentBrush", accent);
        SetBrush(resources, "SystemControlHighlightListAccentLowBrush",
            Color.FromArgb(153, accent.R, accent.G, accent.B));
        SetBrush(resources, "SystemControlHighlightListAccentMediumBrush",
            Color.FromArgb(204, accent.R, accent.G, accent.B));
        SetBrush(resources, "SystemControlHighlightListAccentHighBrush",
            Color.FromArgb(230, accent.R, accent.G, accent.B));

        SetBrush(resources, "AccentFillColorDefaultBrush", accent);
        SetBrush(resources, "AccentFillColorSecondaryBrush", Color.FromArgb(230, accent.R, accent.G, accent.B));
        SetBrush(resources, "AccentFillColorTertiaryBrush", Color.FromArgb(204, accent.R, accent.G, accent.B));
        SetBrush(resources, "AccentFillColorSelectedTextBackgroundBrush", accent);
        SetBrush(resources, "TextOnAccentFillColorPrimaryBrush", textOnAccent);
        SetBrush(resources, "TextOnAccentFillColorSecondaryBrush", textOnAccent);

        SetBrush(resources, "AccentButtonBackground", accent);
        SetBrush(resources, "AccentButtonBackgroundPointerOver", accentLight1);
        SetBrush(resources, "AccentButtonBackgroundPressed", accentDark1);
        SetBrush(resources, "AccentButtonForeground", textOnAccent);
        SetBrush(resources, "AccentButtonForegroundPointerOver", textOnAccent);
        SetBrush(resources, "AccentButtonForegroundPressed", textOnAccent);

        SetBrush(resources, "TextControlBorderBrushFocused", accent);
        SetBrush(resources, "TextControlBorderBrushFocusedPointerOver", accent);
        resources["TextControlSelectionHighlightColor"] = accent;

        SetBrush(resources, "ComboBoxItemPillFillBrush", accent);
        SetBrush(resources, "ListViewItemSelectionIndicatorBrush", accent);
        SetBrush(resources, "ListViewItemSelectionIndicatorPointerOverBrush", accent);
        SetBrush(resources, "ListViewItemSelectionIndicatorPressedBrush", accentDark1);

        SetBrush(resources, "NavigationViewSelectionIndicatorForeground", accent);
        SetBrush(resources, "NavigationViewItemForegroundSelected", accent);
        SetBrush(resources, "NavigationViewItemForegroundSelectedPointerOver", accent);
        SetBrush(resources, "NavigationViewItemForegroundSelectedPressed", accentDark1);
        SetBrush(resources, "NavigationViewItemIconForegroundSelected", accent);
        SetBrush(resources, "NavigationViewItemIconForegroundSelectedPointerOver", accent);
        SetBrush(resources, "NavigationViewItemIconForegroundSelectedPressed", accentDark1);

        SetBrush(resources, "ToggleSwitchFillOn", accent);
        SetBrush(resources, "ToggleSwitchFillOnPointerOver", accentLight1);
        SetBrush(resources, "ToggleSwitchFillOnPressed", accentDark1);
        SetBrush(resources, "ToggleSwitchStrokeOn", accent);
        SetBrush(resources, "ToggleSwitchStrokeOnPointerOver", accentLight1);
        SetBrush(resources, "ToggleSwitchStrokeOnPressed", accentDark1);
        SetBrush(resources, "ToggleSwitchKnobFillOn", textOnAccent);
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        if (resources.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private async void QueueAccentVisualRefresh()
    {
        var version = ++_accentRefreshVersion;
        await Task.Delay(100);

        if (version != _accentRefreshVersion)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (this.Content is FrameworkElement rootElement)
            {
                // Force theme resource re-evaluation across all controls in the visual tree
                var currentTheme = rootElement.RequestedTheme;
                var effectiveTheme = currentTheme == ElementTheme.Default
                    ? GetCurrentSystemTheme()
                    : currentTheme;

                var tempTheme = effectiveTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
                rootElement.RequestedTheme = tempTheme;
                rootElement.RequestedTheme = currentTheme;
            }
        });
    }

    private static Color Blend(Color source, Color target, double amount)
    {
        byte BlendChannel(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromArgb(
            source.A,
            BlendChannel(source.R, target.R),
            BlendChannel(source.G, target.G),
            BlendChannel(source.B, target.B));
    }

    private static Color GetReadableTextColor(Color color)
    {
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
        return luminance > 0.55 ? Black : White;
    }

    private ElementTheme GetCurrentSystemTheme()
    {
        var background = _uiSettings.GetColorValue(UIColorType.Background);
        var luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255;
        return luminance < 0.5 ? ElementTheme.Dark : ElementTheme.Light;
    }

    private static Color White => Color.FromArgb(255, 255, 255, 255);

    private static Color Black => Color.FromArgb(255, 0, 0, 0);
}
