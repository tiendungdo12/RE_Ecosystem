using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RE.WinUI.Core.Settings;
using System;
using System.Threading.Tasks;

namespace RE.WinUI.Views.Settings;

public sealed partial class GeneralSettingsPage : Page
{
    private bool _isInitialized = false;

    public GeneralSettingsPage()
    {
        InitializeComponent();
        Loaded += GeneralSettingsPage_Loaded;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double newWidth = Math.Min(800, e.NewSize.Width);
        if (MainStackPanel != null) MainStackPanel.Width = newWidth;
    }

    private void GeneralSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = SettingsService.Current;
        foreach (ComboBoxItem item in LanguageComboBox.Items)
        {
            if (item.Tag?.ToString() == settings.AppLanguage)
            {
                LanguageComboBox.SelectedItem = item;
                break;
            }
        }

        UpdateLanguage();
        _isInitialized = true;
    }

    public void UpdateLanguage()
    {
        DetailTitle.Text = GetLocalizedString("GeneralSettingsTitle.Text", "General");
        LanguageCard.Header = GetLocalizedString("LanguageCard.Header", "Language");
        LanguageCard.Description = GetLocalizedString("LanguageCard.Description", "Choose display language");
        if (LanguageComboBox.Items.Count > 0 && LanguageComboBox.Items[0] is ComboBoxItem defaultItem)
        {
            defaultItem.Content = GetLocalizedString("LanguageDefault.Content", "System Default");
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

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (LanguageComboBox.SelectedItem is ComboBoxItem item)
        {
            var lang = item.Tag?.ToString() ?? "default";
            SettingsService.Current.AppLanguage = lang;

            // Apply immediately
            SettingsService.ApplyLanguage(lang);

            _ = SettingsService.SaveAsync();

            // Small delay to allow WinUI ResourceLoader to flush its language context cache
            await Task.Delay(50);

            UpdateLanguage();

            if (Microsoft.UI.Xaml.Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ForceReloadPages();
            }
        }
    }
}