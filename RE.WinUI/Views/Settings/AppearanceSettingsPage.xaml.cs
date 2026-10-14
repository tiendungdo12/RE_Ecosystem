using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RE.WinUI.Core.Settings;
using System;
using Windows.UI;

namespace RE.WinUI.Views.Settings;

public sealed partial class AppearanceSettingsPage : Page
{
    private bool _isInitialized = false;

    public AppearanceSettingsPage()
    {
        InitializeComponent();
        Loaded += AppearanceSettingsPage_Loaded;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double newWidth = Math.Min(800, e.NewSize.Width);
        if (MainStackPanel != null) MainStackPanel.Width = newWidth;
    }

    private void AppearanceSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = SettingsService.Current;

        // Set Theme
        foreach (ComboBoxItem item in ThemeComboBox.Items)
        {
            if (item.Tag?.ToString() == settings.AppTheme.ToString())
            {
                ThemeComboBox.SelectedItem = item;
                break;
            }
        }

        // Set Backdrop
        foreach (ComboBoxItem item in BackdropComboBox.Items)
        {
            if (item.Tag?.ToString() == settings.BackdropMaterial)
            {
                BackdropComboBox.SelectedItem = item;
                break;
            }
        }

        // Set Accent Color Toggle
        SystemAccentToggle.IsOn = settings.UseSystemAccentColor;
        CustomColorCard.Visibility = settings.UseSystemAccentColor ? Visibility.Collapsed : Visibility.Visible;

        // Set Custom Color Picker
        if (!settings.UseSystemAccentColor && !string.IsNullOrEmpty(settings.CustomAccentColor))
        {
            try
            {
                var hex = settings.CustomAccentColor.Replace("#", "");
                if (hex.Length == 6 || hex.Length == 8)
                {
                    byte a = hex.Length == 8 ? Convert.ToByte(hex.Substring(0, 2), 16) : (byte)255;
                    int rIndex = hex.Length == 8 ? 2 : 0;
                    byte r = Convert.ToByte(hex.Substring(rIndex, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(rIndex + 2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(rIndex + 4, 2), 16);
                    AccentColorPicker.Color = Color.FromArgb(a, r, g, b);
                }
            }
            catch
            {
            }
        }

        UpdateLanguage();
        _isInitialized = true;
    }

    public void UpdateLanguage()
    {
        DetailTitle.Text = GetLocalizedString("AppearanceSettingsTitle.Text", "Appearance");
        ThemeCard.Header = GetLocalizedString("ThemeCard.Header", "App Theme");
        ThemeCard.Description = GetLocalizedString("ThemeCard.Description", "Select app theme");
        BackdropCard.Header = GetLocalizedString("BackdropCard.Header", "Window Material");
        BackdropCard.Description = GetLocalizedString("BackdropCard.Description", "Background Effect");
        AccentCard.Header = GetLocalizedString("AccentExpander.Header", "Accent Color");
        AccentCard.Description = GetLocalizedString("AccentExpander.Description", "System or custom accent color");
        CustomColorCard.Header = GetLocalizedString("CustomColorCard.Header", "Custom Accent Color");
        SystemAccentToggle.OnContent = GetLocalizedString("SystemAccentToggle.OnContent", "System");
        SystemAccentToggle.OffContent = GetLocalizedString("SystemAccentToggle.OffContent", "Custom");

        // ComboBox items
        if (ThemeComboBox.Items.Count >= 3)
        {
            if (ThemeComboBox.Items[0] is ComboBoxItem c0)
                c0.Content = GetLocalizedString("ThemeDefault.Content", "System Default");
            if (ThemeComboBox.Items[1] is ComboBoxItem c1)
                c1.Content = GetLocalizedString("ThemeLight.Content", "Light");
            if (ThemeComboBox.Items[2] is ComboBoxItem c2) c2.Content = GetLocalizedString("ThemeDark.Content", "Dark");
        }

        if (BackdropComboBox.Items.Count >= 4)
        {
            if (BackdropComboBox.Items[0] is ComboBoxItem b0)
                b0.Content = GetLocalizedString("BackdropMica.Content", "Mica (Windows 11)");
            if (BackdropComboBox.Items[1] is ComboBoxItem b1)
                b1.Content = GetLocalizedString("BackdropMicaAlt.Content", "Mica Alt");
            if (BackdropComboBox.Items[2] is ComboBoxItem b2)
                b2.Content = GetLocalizedString("BackdropAcrylic.Content", "Acrylic");
            if (BackdropComboBox.Items[3] is ComboBoxItem b3)
                b3.Content = GetLocalizedString("BackdropNone.Content", "None");
        }
    }

    private static string GetLocalizedString(string key, string fallback)
    {
        try
        {
            var loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            string val = loader.GetString(key);
            if (string.IsNullOrWhiteSpace(val) && key.Contains('.'))
            {
                val = loader.GetString(key.Replace('.', '/'));
            }

            return string.IsNullOrWhiteSpace(val) ? fallback : val;
        }
        catch
        {
            try
            {
                if (key.Contains('.'))
                {
                    var val =
                        new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader().GetString(key.Replace('.',
                            '/'));
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
            }
            catch
            {
            }

            return fallback;
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (ThemeComboBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<ElementTheme>(item.Tag?.ToString(), out var theme))
        {
            SettingsService.Current.AppTheme = theme;
            _ = SettingsService.SaveAsync();
        }
    }

    private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (BackdropComboBox.SelectedItem is ComboBoxItem item)
        {
            SettingsService.Current.BackdropMaterial = item.Tag?.ToString() ?? "Mica";
            _ = SettingsService.SaveAsync();
        }
    }

    private void SystemAccentToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        SettingsService.Current.UseSystemAccentColor = SystemAccentToggle.IsOn;
        CustomColorCard.Visibility = SystemAccentToggle.IsOn ? Visibility.Collapsed : Visibility.Visible;
        _ = SettingsService.SaveAsync();
    }

    private void AccentColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!_isInitialized) return;
        var color = args.NewColor;
        SettingsService.Current.CustomAccentColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _ = SettingsService.SaveAsync();
    }
}