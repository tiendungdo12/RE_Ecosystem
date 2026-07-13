using System;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.Storage.Pickers;
using RE_CommandCenter.Core.StaticAnalysis;
using System.Linq;

namespace RE_CommandCenter.Views
{
    public sealed partial class TriagePage : Page
    {
        public ObservableCollection<BinwalkResult> BinwalkResults { get; } = new ObservableCollection<BinwalkResult>();
        public ObservableCollection<SectionResult> SectionResults { get; } = new ObservableCollection<SectionResult>();
        public ObservableCollection<string> ImportResults { get; } = new ObservableCollection<string>();

        public TriagePage()
        {
            this.InitializeComponent();
            BinwalkList.ItemsSource = BinwalkResults;
            SectionList.ItemsSource = SectionResults;
            ImportsList.ItemsSource = ImportResults;
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var window = (Application.Current as App)?.MainWindow;
            if (window == null) return;

            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
            
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                FilePathBox.Text = file.Path;
                ScanButton.IsEnabled = true;
            }
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FilePathBox.Text)) return;

            string filePath = FilePathBox.Text;
            
            ScanButton.IsEnabled = false;
            BrowseButton.IsEnabled = false;
            DashboardPanel.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;

            string jsonResult = "";
            try
            {
                jsonResult = await TriageOrchestrator.RunTriageAsync(filePath);
            }
            catch (Exception ex)
            {
                jsonResult = $"{{\"error\": \"{ex.Message}\"}}";
            }

            LoadingRing.IsActive = false;
            ScanButton.IsEnabled = true;
            BrowseButton.IsEnabled = true;
            DashboardPanel.Visibility = Visibility.Visible;

            UpdateDashboard(jsonResult);
        }

        private void UpdateDashboard(string jsonResult)
        {
            RawJsonBox.Text = jsonResult;
            BinwalkResults.Clear();

            try
            {
                var root = JsonNode.Parse(jsonResult);
                if (root == null) return;

                // Basic
                FileSizeText.Text = root["size"]?.ToString() + " bytes";
                MagicText.Text = root["magic"]?.ToString();
                Md5Text.Text = root["hashes"]?["md5"]?.ToString() ?? "N/A";
                Sha256Text.Text = root["hashes"]?["sha256"]?.ToString() ?? "N/A";

                // Architecture
                PlatformText.Text = $"{root["platform"]} ({root["architecture"]})";
                
                // Entropy
                double entropy = (double?)root["entropy"]?["overall"] ?? 0;
                EntropyBar.Value = entropy;
                EntropyText.Text = entropy.ToString("0.00");
                if (entropy > 7.0) EntropyBar.Foreground = new SolidColorBrush(Colors.Red);
                else EntropyBar.Foreground = new SolidColorBrush(Colors.Green);

                SectionResults.Clear();
                var sectionsNode = root["entropy"]?["sections"];
                if (sectionsNode is JsonArray sectionsArray)
                {
                    foreach (var sec in sectionsArray)
                    {
                        string secName = sec?["name"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(secName)) secName = "[UNNAMED]";
                        
                        SectionResults.Add(new SectionResult
                        {
                            Name = secName.Trim(),
                            Entropy = (double?)sec?["entropy"] ?? 0,
                            RawSize = (long?)sec?["raw_size"] ?? 0
                        });
                    }
                }

                // DIE, LIEF & Imports
                string packerStatus = root["packer"]?.ToString() ?? "None";
                string dieAnalysis = root["die_analysis"]?.ToString() ?? "";
                
                if (!string.IsNullOrWhiteSpace(dieAnalysis))
                {
                    PackerText.Text = $"Heuristic: {packerStatus}\n\n{dieAnalysis.Trim()}";
                }
                else
                {
                    PackerText.Text = packerStatus;
                }
                
                ImportResults.Clear();
                var suspiciousNode = root["imports_summary"]?["suspicious"];
                if (suspiciousNode is JsonArray suspArray)
                {
                    foreach (var imp in suspArray)
                    {
                        string impStr = imp?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(impStr))
                        {
                            ImportResults.Add(impStr);
                        }
                    }
                }
                
                SuspiciousImportsText.Text = ImportResults.Count.ToString();
                SuspiciousImportsText.Foreground = ImportResults.Count > 0 ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.Green);
                PipelineText.Text = root["recommended_pipeline"]?.ToString() ?? "manual.review";

                // Authenticode
                var authNode = root["authenticode"];
                if (authNode != null && authNode is JsonObject authObj && authObj.ContainsKey("Status"))
                {
                    string? status = authObj["Status"]?.ToString();
                    AuthText.Text = status;
                    AuthSubjectText.Text = authObj["Subject"]?.ToString() ?? "N/A";
                    
                    if (status == "Valid")
                    {
                        AuthBadge.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 40, 167, 69)); // Green
                        AuthText.Foreground = new SolidColorBrush(Colors.White);
                    }
                    else if (status == "NotSigned")
                    {
                        AuthBadge.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 108, 117, 125)); // Gray
                        AuthText.Foreground = new SolidColorBrush(Colors.White);
                    }
                    else
                    {
                        AuthBadge.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 220, 53, 69)); // Red
                        AuthText.Foreground = new SolidColorBrush(Colors.White);
                    }
                }
                else
                {
                    AuthText.Text = "Not Applicable";
                    AuthSubjectText.Text = "N/A";
                    AuthBadge.Background = new SolidColorBrush(Colors.Transparent);
                    AuthText.Foreground = new SolidColorBrush(Colors.Gray);
                }

                // Binwalk Deep Scan
                var deepScanNode = root["entropy"]?["deep_scan"];
                if (deepScanNode is JsonArray deepScanArray)
                {
                    foreach (var item in deepScanArray)
                    {
                        BinwalkResults.Add(new BinwalkResult
                        {
                            Offset = item?["offset"]?.ToString() ?? "0",
                            Description = item?["description"]?.ToString() ?? "Unknown"
                        });
                    }
                }
                else
                {
                    BinwalkResults.Add(new BinwalkResult { Offset = "-", Description = "No deep scan results." });
                }
            }
            catch (Exception ex)
            {
                StatusBanner.Severity = InfoBarSeverity.Error;
                StatusBanner.Title = "Error Parsing JSON";
                StatusBanner.Message = ex.Message;
            }
        }
    }

    public class BinwalkResult
    {
        public string Offset { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class SectionResult
    {
        public string Name { get; set; } = "";
        public double Entropy { get; set; }
        public long RawSize { get; set; }
        public string RawSizeDisplay => $"{RawSize} bytes";
    }
}
