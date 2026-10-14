using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using RE.WinUI.Views.Settings;
using System;
using System.Threading.Tasks;

namespace RE.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    public Frame ContentFrame => SettingsFrame;

    public bool IsShowingDetail => SettingsFrame != null && SettingsFrame.CanGoBack;

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (SettingsFrame.Content == null)
        {
            SettingsFrame.Navigated += SettingsFrame_Navigated;
            SettingsFrame.Navigate(typeof(SettingsMainPage));
        }
    }

    private void SettingsFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (Microsoft.UI.Xaml.Application.Current is App app && app.MainWindow is MainWindow mainWindow)
        {
            mainWindow.UpdateBackEnabled();
        }
    }

    public bool TryGoBack()
    {
        if (SettingsFrame != null && SettingsFrame.CanGoBack)
        {
            SettingsFrame.GoBack();
            return true;
        }

        return false;
    }

    public Task<bool> TryGoBackAsync()
    {
        return Task.FromResult(TryGoBack());
    }

    public void NavigateToMain()
    {
        if (SettingsFrame != null && SettingsFrame.CanGoBack)
        {
            while (SettingsFrame.CanGoBack)
            {
                SettingsFrame.GoBack();
            }

            if (Microsoft.UI.Xaml.Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                mainWindow.UpdateBackEnabled();
            }
        }
    }

    public void UpdateLanguage()
    {
        if (SettingsFrame.Content is SettingsMainPage mainPage)
        {
            mainPage.UpdateLanguage();
        }
        else if (SettingsFrame.Content is GeneralSettingsPage generalPage)
        {
            generalPage.UpdateLanguage();
        }
        else if (SettingsFrame.Content is AppearanceSettingsPage appearancePage)
        {
            appearancePage.UpdateLanguage();
        }
        else if (SettingsFrame.Content is AISettingsPage aiPage)
        {
            aiPage.UpdateLanguage();
        }
    }
}