using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RE.Domain.Models;
using RE.Core.Services;
using RE.Core.Abstractions;

namespace RE.WinUI.ViewModels
{
    public class GroupedImportCategory
    {
        public string CategoryKey { get; set; } = string.Empty;
        public string DisplayTitle { get; set; } = string.Empty;
        public ObservableCollection<string> Apis { get; } = new();
    }

    public partial class TriageViewModel : ObservableObject
    {
        private readonly ITriagePipeline _triagePipeline;
        private readonly IFilePicker _filePicker;

        [ObservableProperty] public partial string FilePath { get; set; } = string.Empty;
        [ObservableProperty] public partial bool IsScanning { get; set; }
        [ObservableProperty] public partial string ScanStatusText { get; set; } = string.Empty;
        [ObservableProperty] public partial bool IsDashboardVisible { get; set; } = false;
        [ObservableProperty] public partial string RawJson { get; set; } = string.Empty;
        [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsActiveAnalysisVisible))]
        [NotifyPropertyChangedFor(nameof(ActiveAnalysisOpacity))]
        [NotifyPropertyChangedFor(nameof(ScanLibraryOpacity))]
        public partial bool IsScanLibraryVisible { get; set; } = false;

        public bool IsActiveAnalysisVisible => !IsScanLibraryVisible;

        public double ActiveAnalysisOpacity => !IsScanLibraryVisible ? 1.0 : 0.6;
        public double ScanLibraryOpacity => IsScanLibraryVisible ? 1.0 : 0.6;

        [ObservableProperty] public partial string SearchQuery { get; set; } = string.Empty;

        partial void OnIsScanLibraryVisibleChanged(bool value)
        {
            if (value)
            {
                _ = SearchScanLibraryAsync();
            }
        }

        partial void OnSearchQueryChanged(string value)
        {
            _ = SearchScanLibraryAsync();
        }

        // Metadata & Hashes
        [ObservableProperty] public partial string FileSize { get; set; } = "0 bytes";
        [ObservableProperty] public partial string Magic { get; set; } = string.Empty;
        [ObservableProperty] public partial string Md5 { get; set; } = "N/A";
        [ObservableProperty] public partial string Sha256 { get; set; } = "N/A";
        [ObservableProperty] public partial string PlatformInfo { get; set; } = string.Empty;
        [ObservableProperty] public partial double Entropy { get; set; }
        [ObservableProperty] public partial bool IsHighEntropy { get; set; }
        [ObservableProperty] public partial string PackerInfo { get; set; } = "None";
        [ObservableProperty] public partial string EngineInfo { get; set; } = "Unknown";
        [ObservableProperty] public partial int SuspiciousImportsCount { get; set; }
        [ObservableProperty] public partial int TotalImportsCount { get; set; }
        [ObservableProperty] public partial bool HasNoSuspiciousImports { get; set; } = true;
        [ObservableProperty] public partial string RecommendedPipeline { get; set; } = "manual.review";
        [ObservableProperty] public partial string AuthStatus { get; set; } = "Not Applicable";
        [ObservableProperty] public partial string AuthSubject { get; set; } = "N/A";

        // Risk Assessment Properties
        [ObservableProperty] public partial int RiskScore { get; set; } = 0;
        [ObservableProperty] public partial string RiskLevel { get; set; } = "CLEAN";
        [ObservableProperty] public partial string RiskLevelDisplayText { get; set; } = "CLEAN";
        [ObservableProperty] public partial bool HasRiskFactors { get; set; } = false;
        [ObservableProperty] public partial bool HasNoRiskFactors { get; set; } = true;

        // Android Metadata Properties
        [ObservableProperty] public partial bool IsAndroidFile { get; set; } = false;
        [ObservableProperty] public partial string AndroidPackageName { get; set; } = string.Empty;
        [ObservableProperty] public partial string AndroidVersion { get; set; } = string.Empty;
        [ObservableProperty] public partial string AndroidMainActivity { get; set; } = string.Empty;
        [ObservableProperty] public partial string AndroidNativeArchs { get; set; } = string.Empty;
        [ObservableProperty] public partial int AndroidDexCount { get; set; } = 0;
        [ObservableProperty] public partial int AndroidDangerousPermsCount { get; set; } = 0;

        public ObservableCollection<string> RiskFactors { get; } = new();
        public ObservableCollection<AndroidDangerousPermission> DangerousPermissions { get; } = new();
        public ObservableCollection<GroupedImportCategory> CategorizedImports { get; } = new();
        public ObservableCollection<SectionInfo> Sections { get; } = new();
        public ObservableCollection<BinwalkEntry> BinwalkResults { get; } = new();
        public ObservableCollection<string> SuspiciousImports { get; } = new();
        public ObservableCollection<KeyValuePair<string, string>> DieDetails { get; } = new();
        public ObservableCollection<TriageResult> RecentScans { get; } = new();

        private readonly IKnowledgeStore _knowledgeStore;
        private CancellationTokenSource? _scanCts;

        public TriageViewModel(ITriagePipeline triagePipeline, IFilePicker filePicker, IKnowledgeStore knowledgeStore)
        {
            _triagePipeline = triagePipeline;
            _filePicker = filePicker;
            _knowledgeStore = knowledgeStore;
        }

        [RelayCommand]
        private async Task BrowseAsync()
        {
            var file = await _filePicker.PickFileAsync();
            if (file != null)
            {
                FilePath = file;
            }
        }

        [RelayCommand]
        public void ShowActiveAnalysis()
        {
            IsScanLibraryVisible = false;
        }

        [RelayCommand]
        public void ShowScanLibrary()
        {
            IsScanLibraryVisible = true;
        }

        [RelayCommand]
        private async Task LoadRecentScansAsync()
        {
            await SearchScanLibraryAsync();
        }

        [RelayCommand]
        private async Task SearchScanLibraryAsync()
        {
            try
            {
                var items = await _knowledgeStore.SearchTriageResultsAsync(SearchQuery, 50);
                RecentScans.Clear();
                foreach (var item in items)
                {
                    RecentScans.Add(item);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to search library: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task DeleteScanAsync(TriageResult? result)
        {
            if (result == null) return;
            try
            {
                await _knowledgeStore.DeleteTriageResultAsync(result.FilePath);
                RecentScans.Remove(result);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to delete scan: {ex.Message}";
            }
        }

        [RelayCommand]
        private void SelectRecentScan(TriageResult? result)
        {
            if (result == null) return;
            FilePath = result.FilePath;
            PopulateDashboard(result);
            IsScanLibraryVisible = false;
        }

        private static bool _isFirstScan = true;

        [RelayCommand]
        private async Task ScanAsync()
        {
            if (string.IsNullOrWhiteSpace(FilePath)) return;

            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;

            IsScanning = true;
            ScanStatusText = _isFirstScan
                ? "Loading static analysis databases into memory... (This only happens once)"
                : "Extracting file metadata, signatures and analyzing risk...";
            _isFirstScan = false;
            IsDashboardVisible = false;
            ErrorMessage = string.Empty;

            try
            {
                var result = await _triagePipeline.RunTriageAsync(FilePath, token);
                token.ThrowIfCancellationRequested();
                await _knowledgeStore.SaveTriageResultAsync(result);
                PopulateDashboard(result);
            }
            catch (OperationCanceledException)
            {
                ScanStatusText = "Đã hủy phân tích.";
                IsDashboardVisible = false;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                RawJson = $"{{\"error\": \"{ex.Message}\"}}";
                IsDashboardVisible = false;
            }
            finally
            {
                IsScanning = false;
            }
        }

        [RelayCommand]
        private void CancelScan()
        {
            if (IsScanning && _scanCts != null)
            {
                _scanCts.Cancel();
                ScanStatusText = "Đang hủy phân tích...";
            }
        }

        private void PopulateDashboard(TriageResult result)
        {
            FileSize = $"{result.Size:N0} bytes";

            var magicStr = result.Magic ?? "";
            var magicParts = magicStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Magic = string.Join(" ", magicParts.Take(4));

            Md5 = result.Hashes.TryGetValue("md5", out var md5) ? md5 : "N/A";
            Sha256 = result.Hashes.TryGetValue("sha256", out var sha256) ? sha256 : "N/A";
            PlatformInfo = $"{result.Platform} ({result.Architecture})";
            Entropy = result.Entropy;
            IsHighEntropy = result.Entropy >= 7.2;

            PackerInfo = string.IsNullOrEmpty(result.Packer) ? "None" : result.Packer;
            EngineInfo = string.IsNullOrEmpty(result.Engine) ? "Unknown" : result.Engine;

            // Risk Assessment
            RiskScore = result.RiskScore;
            RiskLevel = result.RiskLevel;
            RiskLevelDisplayText = RiskLevel switch
            {
                "CRITICAL" => "🔴 NGUY HIỂM CAO",
                "HIGH" => "🟠 RỦI RO CAO",
                "MEDIUM" => "🟡 TRUNG BÌNH",
                "LOW" => "🟢 THẤP",
                "CLEAN" => "🛡️ AN TOÀN",
                "UNKNOWN" => "⚠️ CHƯA XÁC ĐỊNH / LỖI",
                _ => $"❓ {RiskLevel}"
            };

            RiskFactors.Clear();
            foreach (var rf in result.RiskFactors)
            {
                RiskFactors.Add(rf);
            }

            HasRiskFactors = RiskFactors.Count > 0;
            HasNoRiskFactors = !HasRiskFactors;

            // Android Profile
            IsAndroidFile = result.AndroidMetadata != null || result.FileType == "apk" || result.Platform == "android";
            DangerousPermissions.Clear();
            if (result.AndroidMetadata != null)
            {
                AndroidPackageName = result.AndroidMetadata.PackageName;
                AndroidVersion = $"{result.AndroidMetadata.VersionName} (Build {result.AndroidMetadata.VersionCode})";
                AndroidMainActivity = result.AndroidMetadata.MainActivity;
                AndroidNativeArchs = result.AndroidMetadata.NativeArchitectures.Count > 0
                    ? string.Join(", ", result.AndroidMetadata.NativeArchitectures)
                    : "None (DEX only)";
                AndroidDexCount = result.AndroidMetadata.DexCount;

                foreach (var dp in result.AndroidMetadata.DangerousPermissions)
                {
                    DangerousPermissions.Add(dp);
                }

                AndroidDangerousPermsCount = DangerousPermissions.Count;
            }
            else
            {
                AndroidPackageName = "N/A";
                AndroidVersion = "N/A";
                AndroidMainActivity = "N/A";
                AndroidNativeArchs = "N/A";
                AndroidDexCount = 0;
                AndroidDangerousPermsCount = 0;
            }

            // DIE Details
            DieDetails.Clear();
            if (!string.IsNullOrWhiteSpace(result.DieAnalysis))
            {
                var lines = result.DieAnalysis.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = trimmed.Substring(0, colonIndex).Trim();
                        var value = trimmed.Substring(colonIndex + 1).Trim();
                        DieDetails.Add(new KeyValuePair<string, string>(key, value));
                    }
                    else
                    {
                        DieDetails.Add(new KeyValuePair<string, string>("Format", trimmed));
                    }
                }
            }

            // Suspicious Imports & Categorized APIs
            SuspiciousImports.Clear();
            foreach (var imp in result.SuspiciousImports) SuspiciousImports.Add(imp);
            SuspiciousImportsCount = SuspiciousImports.Count;
            TotalImportsCount = result.TotalImportsCount;
            HasNoSuspiciousImports = SuspiciousImportsCount == 0;

            CategorizedImports.Clear();
            foreach (var kvp in result.SuspiciousImportsByCategory)
            {
                var group = new GroupedImportCategory
                {
                    CategoryKey = kvp.Key,
                    DisplayTitle = GetCategoryTitle(kvp.Key)
                };
                foreach (var api in kvp.Value)
                {
                    group.Apis.Add(api);
                }

                CategorizedImports.Add(group);
            }

            RecommendedPipeline = result.RecommendedPipeline;

            AuthStatus = result.Signature.Status;
            AuthSubject = result.Signature.Subject ?? "N/A";

            // Sections
            Sections.Clear();
            foreach (var sec in result.Sections) Sections.Add(sec);

            // Binwalk Deep Scan
            BinwalkResults.Clear();
            if (result.DeepScanResults.Count == 0)
            {
                BinwalkResults.Add(new BinwalkEntry { Offset = "-", Description = "No deep scan results." });
            }
            else
            {
                foreach (var bw in result.DeepScanResults) BinwalkResults.Add(bw);
            }

            RawJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            IsDashboardVisible = true;
        }

        private static string GetCategoryTitle(string key) => key switch
        {
            "memory_injection" => "Memory Injection (Tiêm mã bộ nhớ)",
            "dynamic_loading" => "Dynamic Loading (Nạp hàm động)",
            "process_execution" => "Process Execution (Can thiệp tiến trình)",
            "anti_analysis" => "Anti-Analysis / Debug (Chống dịch ngược)",
            "persistence_registry" => "Persistence & Registry (Tự duy trì)",
            "crypto_ransomware" => "Crypto / Ransomware (Mã hóa)",
            "network_c2" => "Network / C2 (Giao tiếp mạng)",
            "privilege_escalation" => "Privilege Escalation (Leo thang đặc quyền)",
            "file_evasion_dropper" => "File Dropper / Evasion (Thao tác tệp)",
            _ => key.Replace("_", " ").ToUpper()
        };
    }
}