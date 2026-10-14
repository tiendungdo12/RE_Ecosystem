using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using RE.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace RE.WinUI.Views
{
    public sealed partial class TriagePage : Page
    {
        public TriageViewModel ViewModel { get; }

        public TriagePage()
        {
            this.InitializeComponent();
            ViewModel = (Microsoft.UI.Xaml.Application.Current as App)!.Services.GetRequiredService<TriageViewModel>();
            this.DataContext = ViewModel;
        }

        private void Page_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            // Only advertise a copy operation for actual filesystem items.
            // This prevents taskbar/URL drags from appearing as a supported drop.
            e.AcceptedOperation =
                e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)
                    ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
                    : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            e.Handled = true;
        }

        private async void Page_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0)
                {
                    ViewModel.FilePath = items[0].Path;
                }
            }

            e.Handled = true;

            // Bring window back to front if it minimized/lost focus during drag drop
            var window = (Microsoft.UI.Xaml.Application.Current as App)?.MainWindow;
            if (window != null)
            {
                window.Activate();
            }
        }

        private void RootScrollViewer_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
        {
            // Set the width of the main container dynamically based on the scrollviewer's viewport width.
            // 48 is to account for the Margin="24" on both sides of the MainContainerGrid to avoid horizontal scrolling.
            MainContainerGrid.Width = System.Math.Min(1200, System.Math.Max(0, e.NewSize.Width - 48));
        }


        public static Microsoft.UI.Xaml.Visibility BoolToVisibility(bool val) =>
            val ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public static bool InvertBool(bool val) => !val;

        public static double BoolToOpacity(bool val) => val ? 1.0 : 0.6;

        private void CopyMd5_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string md5 && !string.IsNullOrEmpty(md5))
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(md5);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            }
        }

        private void ReopenScan_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is RE.Domain.Models.TriageResult result)
            {
                ViewModel.SelectRecentScanCommand.Execute(result);
            }
        }

        private void DeleteScan_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is RE.Domain.Models.TriageResult result)
            {
                ViewModel.DeleteScanCommand.Execute(result);
            }
        }
    }
}