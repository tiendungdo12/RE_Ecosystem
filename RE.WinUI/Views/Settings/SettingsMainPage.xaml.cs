using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;

namespace RE.WinUI.Views.Settings;

public sealed partial class SettingsMainPage : Page
{
    public SettingsMainPage()
    {
        InitializeComponent();
        Loaded += SettingsMainPage_Loaded;
    }

    private void SettingsMainPage_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateLanguage();
    }

    public void UpdateLanguage()
    {
        SettingsTitle.Text = GetLocalizedString("SettingsTitle.Text", "Settings");
        NavGeneralCard.Header = GetLocalizedString("NavGeneralCard.Header", "General");
        NavGeneralCard.Description = GetLocalizedString("NavGeneralCard.Description", "Language");
        NavAppearanceCard.Header = GetLocalizedString("NavAppearanceCard.Header", "Appearance");
        NavAppearanceCard.Description =
            GetLocalizedString("NavAppearanceCard.Description", "Theme, Window material, Accent color");
        NavAICard.Header = GetLocalizedString("NavAICard.Header", "AI & Models");
        NavAICard.Description = GetLocalizedString("NavAICard.Description", "Antigravity, Ollama, API keys");
        NavMcpCard.Header = GetLocalizedString("NavMcpCard.Header", "MCP Connections");
        NavMcpCard.Description =
            GetLocalizedString("NavMcpCard.Description", "Manage and test MCP tools (IDA Pro, Triage, etc.)");
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

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double newWidth = Math.Min(800, e.NewSize.Width);
        if (MasterStackPanel != null) MasterStackPanel.Width = newWidth;
    }

    private void NavCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CommunityToolkit.WinUI.Controls.SettingsCard card && card.Tag is string tag && Frame != null)
        {
            var transition = new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight };
            switch (tag)
            {
                case "General":
                    Frame.Navigate(typeof(GeneralSettingsPage), null, transition);
                    break;
                case "Appearance":
                    Frame.Navigate(typeof(AppearanceSettingsPage), null, transition);
                    break;
                case "AI":
                    Frame.Navigate(typeof(AISettingsPage), null, transition);
                    break;
                case "MCP":
                    Frame.Navigate(typeof(McpSettingsPage), null, transition);
                    break;
            }
        }
    }
}