using System.Collections.Generic;
using System.Linq;

namespace RE.Domain.Models;

public class TriageResult
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => string.IsNullOrEmpty(FilePath) ? string.Empty : System.IO.Path.GetFileName(FilePath);
    public long Size { get; set; }
    public string Magic { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string Packer { get; set; } = "unknown";
    public string Engine { get; set; } = "unknown";
    public string RecommendedPipeline { get; set; } = "manual.review";
    public string DieAnalysis { get; set; } = string.Empty;
    public double Entropy { get; set; }
    public Dictionary<string, string> Hashes { get; set; } = new();
    public string MD5Hash => Hashes.TryGetValue("md5", out var md5) ? md5 : "N/A";
    public string SHA256Hash => Hashes.TryGetValue("sha256", out var sha) ? sha : "N/A";
    public SignatureResult Signature { get; set; } = new();
    public List<SectionInfo> Sections { get; set; } = new();
    public List<string> SuspiciousImports { get; set; } = new();
    public Dictionary<string, List<string>> SuspiciousImportsByCategory { get; set; } = new();
    public List<string> Libraries { get; set; } = new();
    public int TotalImportsCount { get; set; }
    public List<BinwalkEntry> DeepScanResults { get; set; } = new();
    public int RiskScore { get; set; } = 0;
    public string RiskLevel { get; set; } = "CLEAN";
    public List<string> RiskFactors { get; set; } = new();
    public AndroidMetadata? AndroidMetadata { get; set; }
    public string MarkdownReport { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public bool IsTimeout => ErrorCode == "TIMEOUT" || Status == TriageStatus.TimedOut;

    /// <summary>
    /// Explicit execution status of the pipeline (Completed, TimedOut, PartialFailure, Cancelled).
    /// </summary>
    public TriageStatus Status { get; set; } = TriageStatus.Completed;

    /// <summary>
    /// True if the target file exceeds the 200 MiB limit and static analysis was performed on the first 200 MiB.
    /// Hashes in C# cover the full file regardless.
    /// </summary>
    public bool SizeTruncated { get; set; } = false;

    /// <summary>
    /// Specific subprocess analyzer error (LIEF, AXML, or execution failure).
    /// </summary>
    public string? SubprocessAnalyzerError { get; set; }

    /// <summary>
    /// Detailed analyzer errors per component (e.g. "die", "lief", "axml", "binwalk").
    /// </summary>
    public Dictionary<string, string> AnalyzerErrors { get; set; } = new();

    /// <summary>
    /// Tạo bản tóm tắt token-optimized cho AI prompt.
    /// Chỉ giữ lại: metadata chính, risk assessment, flagged sections, categorized imports, android metadata.
    /// Bỏ qua: raw sections bình thường, full export list, raw JSON report.
    /// </summary>
    public Dictionary<string, object?> ToAIFriendlyContext()
    {
        var ctx = new Dictionary<string, object?>
        {
            ["file_type"] = FileType,
            ["platform"] = Platform,
            ["architecture"] = Architecture,
            ["packer"] = Packer,
            ["engine"] = Engine,
            ["recommended_pipeline"] = RecommendedPipeline,
            ["risk_score"] = RiskScore,
            ["risk_level"] = RiskLevel,
            ["risk_factors"] = RiskFactors.Count > 0 ? RiskFactors : null,
            ["entropy_overall"] = Entropy,
            ["die_analysis"] = !string.IsNullOrEmpty(DieAnalysis) ? DieAnalysis : null,
        };

        // Only include flagged/anomalous sections to save tokens
        var flagged = Sections
            .Where(s => s.IsFlagged || s.Entropy > 7.2)
            .Select(s => new Dictionary<string, object?>
            {
                ["name"] = s.Name,
                ["entropy"] = s.Entropy,
                ["anomalies"] = s.Anomalies.Count > 0 ? string.Join(", ", s.Anomalies) : null,
            })
            .ToList();
        if (flagged.Count > 0)
            ctx["flagged_sections"] = flagged;

        // Categorized suspicious imports
        if (SuspiciousImportsByCategory.Count > 0)
        {
            ctx["suspicious_imports"] = SuspiciousImportsByCategory;
            ctx["total_imports"] = TotalImportsCount;
            ctx["suspicious_count"] = SuspiciousImports.Count;
        }

        // Authenticode
        if (Signature != null && !string.IsNullOrEmpty(Signature.Status))
            ctx["authenticode_status"] = Signature.Status;

        // Android metadata
        if (AndroidMetadata != null)
        {
            ctx["android"] = new Dictionary<string, object?>
            {
                ["package_name"] = AndroidMetadata.PackageName,
                ["main_activity"] = AndroidMetadata.MainActivity,
                ["engine"] = AndroidMetadata.Engine,
                ["packer"] = AndroidMetadata.Packer,
                ["dangerous_permissions_count"] = AndroidMetadata.DangerousPermissions.Count,
                ["dangerous_permissions"] = AndroidMetadata.DangerousPermissions.Count > 0
                    ? AndroidMetadata.DangerousPermissions.Select(p => $"{p.Name} ({p.Risk})").ToList()
                    : null,
            };
        }

        return ctx;
    }
}