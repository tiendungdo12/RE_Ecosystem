using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RE_CommandCenter.Core.Settings;
using System;
using System.Linq;
using Windows.UI;

namespace RE_CommandCenter.Views;

public sealed partial class SettingsPage : Page
{
    private bool _isInitialized = false;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = SettingsService.Current;

        // Set Theme
        foreach (ComboBoxItem item in ThemeComboBox.Items)
        {
            if (item.Tag.ToString() == settings.AppTheme.ToString())
            {
                ThemeComboBox.SelectedItem = item;
                break;
            }
        }

        // Set Backdrop
        foreach (ComboBoxItem item in BackdropComboBox.Items)
        {
            if (item.Tag.ToString() == settings.BackdropMaterial)
            {
                BackdropComboBox.SelectedItem = item;
                break;
            }
        }

        // Set Accent Color Toggle
        SystemAccentToggle.IsOn = settings.UseSystemAccentColor;
        CustomColorPanel.Visibility = settings.UseSystemAccentColor ? Visibility.Collapsed : Visibility.Visible;

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
            catch { }
        }

        _isInitialized = true;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (ThemeComboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<ElementTheme>(item.Tag.ToString(), out var theme))
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
            SettingsService.Current.BackdropMaterial = item.Tag.ToString() ?? "Mica";
            _ = SettingsService.SaveAsync();
        }
    }

    private void SystemAccentToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        SettingsService.Current.UseSystemAccentColor = SystemAccentToggle.IsOn;
        CustomColorPanel.Visibility = SystemAccentToggle.IsOn ? Visibility.Collapsed : Visibility.Visible;
        _ = SettingsService.SaveAsync();
    }

    private void AccentColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!_isInitialized) return;
        var c = args.NewColor;
        SettingsService.Current.CustomAccentColor = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        _ = SettingsService.SaveAsync();
    }
}
