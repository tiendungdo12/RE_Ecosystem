using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RE.Core.Abstractions;
using RE.Core.StaticAnalysis;
using RE.Domain.Models;

namespace RE.Core.Services
{
    /// <summary>
    /// Canonical Phase 1 Deterministic Triage Service.
    /// Runs 100% deterministically without calling LLM and without passing through MCP.
    /// Uses native C# analyzers (HashCalculator), native P/Invoke (Authenticode, DIE, Binwalk),
    /// and direct subprocess execution (DirectTriageSubprocessAnalyzer for LIEF/AXML).
    /// </summary>
    public class TriageService : ITriagePipeline
    {
        private readonly ISignatureVerifier _signatureVerifier;
        private readonly IPlatformEnvironment _platform;
        private readonly IDirectTriageSubprocessAnalyzer _subprocessAnalyzer;
        private readonly Func<string, string?> _binwalkScanner;
        private readonly Func<string, string, string?> _dieScanner;

        public TriageService(
            ISignatureVerifier signatureVerifier,
            IPlatformEnvironment platform,
            IDirectTriageSubprocessAnalyzer? subprocessAnalyzer = null,
            Func<string, string?>? binwalkScanner = null,
            Func<string, string, string?>? dieScanner = null)
        {
            _signatureVerifier = signatureVerifier;
            _platform = platform;
            _subprocessAnalyzer = subprocessAnalyzer ?? new DirectTriageSubprocessAnalyzer(platform);
            _binwalkScanner = binwalkScanner ?? BinwalkWrapper.Scan;
            _dieScanner = dieScanner ?? DieWrapper.Scan;
        }

        public async Task<TriageResult> RunTriageAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var triageResult = new TriageResult { FilePath = filePath };
            if (!File.Exists(filePath))
            {
                triageResult.Status = TriageStatus.PartialFailure;
                triageResult.RecommendedPipeline = "error.file_not_found";
                triageResult.RiskLevel = "UNKNOWN";
                triageResult.RiskFactors.Add("Tệp tin không tồn tại trên đĩa.");
                triageResult.MarkdownReport = $"# 🛡️ Báo Cáo Phân Tích Tĩnh — Executive Triage\n**Tệp tin:** `{filePath}`  \n**Đánh giá Rủi ro:** ❓ **CHƯA XÁC ĐỊNH (0/100)**\n\n> ❌ **Lỗi:** Tệp không tồn tại hoặc không thể truy cập.";
                return triageResult;
            }

            try
            {
                // 1. Calculate Hashes natively (System.Security.Cryptography) with cancellation support
                var hashes = await HashCalculator.CalculateHashesAsync(filePath, cancellationToken);
                if (hashes["error"] != null && !string.IsNullOrEmpty(hashes["error"]?.ToString()))
                {
                    string hashErr = hashes["error"]!.ToString();
                    triageResult.AnalyzerErrors["hash"] = hashErr;
                    triageResult.RiskFactors.Add($"[Lỗi băm tệp] {hashErr}");
                    triageResult.Status = TriageStatus.PartialFailure;
                }
                else
                {
                    triageResult.Hashes["md5"] = hashes["md5"]?.ToString() ?? "";
                    triageResult.Hashes["sha1"] = hashes["sha1"]?.ToString() ?? "";
                    triageResult.Hashes["sha256"] = hashes["sha256"]?.ToString() ?? "";
                }

                if (string.IsNullOrEmpty(triageResult.Hashes.GetValueOrDefault("sha256")))
                {
                    if (!triageResult.AnalyzerErrors.ContainsKey("hash"))
                    {
                        triageResult.AnalyzerErrors["hash"] = "Không thể tạo mã băm SHA256.";
                        triageResult.RiskFactors.Add("[Lỗi băm tệp] Không thể tạo mã băm SHA256.");
                    }
                    triageResult.Status = TriageStatus.PartialFailure;
                }

                // File size from filesystem (true size, not truncated)
                try
                {
                    triageResult.Size = new FileInfo(filePath).Length;
                    const long LargeFileLimit = 200L * 1024 * 1024;
                    if (triageResult.Size > LargeFileLimit)
                    {
                        triageResult.SizeTruncated = true;
                        triageResult.RiskFactors.Add($"Tệp tin lớn hơn 200MB ({triageResult.Size:N0} bytes); phân tích tĩnh chuyên sâu thực hiện trên 200MB đầu tiên.");
                    }
                }
                catch
                {
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 2. Direct Subprocess Analysis (LIEF & AXML - NO MCP, NO LLM)
                string? knownMd5 = triageResult.Hashes.GetValueOrDefault("md5");
                string? knownSha256 = triageResult.Hashes.GetValueOrDefault("sha256");
                var directResult = await _subprocessAnalyzer.AnalyzeAsync(filePath, knownMd5, knownSha256, cancellationToken);

                bool hasSubprocessError = directResult.TryGetProperty("error", out var errProp);
                bool isTimeout = (directResult.TryGetProperty("is_timeout", out var toProp) && toProp.GetBoolean())
                    || (directResult.TryGetProperty("error_code", out var ecProp) && ecProp.GetString() == "TIMEOUT");

                if (isTimeout)
                {
                    string errMsg = errProp.GetString() ?? "Subprocess triage timed out after 15 seconds.";
                    triageResult.ErrorCode = "TIMEOUT";
                    triageResult.Status = TriageStatus.TimedOut;
                    triageResult.SubprocessAnalyzerError = errMsg;
                    triageResult.RiskFactors.Add($"[TIMEOUT] Phân tích cấu trúc (LIEF/AXML) quá thời gian: {errMsg}");
                    triageResult.RiskLevel = "UNKNOWN";
                    triageResult.RecommendedPipeline = "manual.review";
                }
                else if (hasSubprocessError)
                {
                    string errMsg = errProp.GetString() ?? "Unknown subprocess analysis error";
                    triageResult.ErrorCode = "SUBPROCESS_ERROR";
                    triageResult.Status = TriageStatus.PartialFailure;
                    triageResult.SubprocessAnalyzerError = errMsg;
                    triageResult.RiskFactors.Add($"Phân tích cấu trúc (LIEF/AXML) thất bại: {errMsg}");
                    triageResult.RiskLevel = "UNKNOWN";
                    triageResult.RecommendedPipeline = "manual.review";
                }

                if (directResult.TryGetProperty("size_truncated", out var stProp) && stProp.GetBoolean())
                {
                    triageResult.SizeTruncated = true;
                }

                // Never overwrite actual file size with truncated subprocess buffer length
                if (triageResult.Size == 0 && directResult.TryGetProperty("size", out var sizeProp) && sizeProp.GetInt64() > 0)
                {
                    triageResult.Size = sizeProp.GetInt64();
                }

                // Capture granular analyzer errors if reported
                if (directResult.TryGetProperty("analyzer_errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in errorsProp.EnumerateObject())
                    {
                        string errVal = prop.Value.GetString() ?? "";
                        triageResult.AnalyzerErrors[prop.Name] = errVal;
                        triageResult.RiskFactors.Add($"[{prop.Name.ToUpperInvariant()} Lỗi] {errVal}");
                        triageResult.Status = TriageStatus.PartialFailure;
                        triageResult.RiskLevel = "UNKNOWN";
                    }
                }

                triageResult.Magic = directResult.TryGetProperty("magic", out var magic) ? magic.GetString() ?? "" : "";
                triageResult.FileType = directResult.TryGetProperty("file_type", out var fileType)
                    ? fileType.GetString() ?? ""
                    : "";
                triageResult.Platform = directResult.TryGetProperty("platform", out var platform)
                    ? platform.GetString() ?? ""
                    : "";
                triageResult.Architecture = directResult.TryGetProperty("architecture", out var arch)
                    ? arch.GetString() ?? ""
                    : "";
                triageResult.Packer = directResult.TryGetProperty("packer", out var packer)
                    ? packer.GetString() ?? ""
                    : "unknown";
                triageResult.Engine = directResult.TryGetProperty("engine", out var engine)
                    ? engine.GetString() ?? "unknown"
                    : "unknown";
                if (!hasSubprocessError && !isTimeout)
                {
                    triageResult.RecommendedPipeline = directResult.TryGetProperty("recommended_pipeline", out var rec)
                        ? rec.GetString() ?? "manual.review"
                        : "manual.review";
                }
                triageResult.MarkdownReport = directResult.TryGetProperty("markdown_report", out var md)
                    ? md.GetString() ?? ""
                    : "";

                cancellationToken.ThrowIfCancellationRequested();

                // 3. Authenticode Check (Windows / PE) via native P/Invoke wintrust.dll
                if (triageResult.Platform == "windows" || triageResult.FileType == "pe")
                {
                    try
                    {
                        var authTask = Task.Run(() =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return _signatureVerifier.Verify(filePath);
                        }, cancellationToken);
                        triageResult.Signature = await authTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

                        if (triageResult.Signature != null &&
                            triageResult.Signature.Status != "Valid" &&
                            triageResult.Signature.Status != "NotSigned" &&
                            triageResult.Signature.Status != "NotApplicable")
                        {
                            triageResult.Status = TriageStatus.PartialFailure;
                            triageResult.AnalyzerErrors["authenticode"] = $"Authenticode verification failed: {triageResult.Signature.Status}";
                            triageResult.RiskFactors.Add($"[Chữ ký số không hợp lệ] Mã lỗi xác thực Authenticode: {triageResult.Signature.Status}");
                            triageResult.RiskLevel = "UNKNOWN";
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (TimeoutException)
                    {
                        triageResult.Status = TriageStatus.PartialFailure;
                        triageResult.AnalyzerErrors["authenticode"] = "Authenticode verification timed out after 5 seconds.";
                        triageResult.RiskFactors.Add("[Authenticode Timeout] Quá trình xác thực chữ ký số vượt quá thời gian cho phép.");
                        triageResult.RiskLevel = "UNKNOWN";
                        triageResult.Signature = new SignatureResult { Status = "Timeout", Subject = null };
                    }
                    catch (InvalidOperationException busyEx) when (busyEx.Message.Contains("busy"))
                    {
                        triageResult.Status = TriageStatus.PartialFailure;
                        triageResult.AnalyzerErrors["authenticode"] = busyEx.Message;
                        triageResult.RiskFactors.Add($"[Authenticode Bận] {busyEx.Message}");
                        triageResult.RiskLevel = "UNKNOWN";
                        triageResult.Signature = new SignatureResult { Status = "Busy", Subject = null };
                    }
                    catch (Exception authEx)
                    {
                        triageResult.Status = TriageStatus.PartialFailure;
                        triageResult.AnalyzerErrors["authenticode"] = authEx.Message;
                        triageResult.RiskFactors.Add($"[Authenticode Lỗi] {authEx.Message}");
                        triageResult.RiskLevel = "UNKNOWN";
                        triageResult.Signature = new SignatureResult { Status = "Error", Subject = null };
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 4. DIE Scan via native P/Invoke die_core.dll
                if (!triageResult.SizeTruncated)
                {
                    try
                    {
                        var dieTask = Task.Run(() =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return _dieScanner(filePath, _platform.DieDbPath);
                        }, cancellationToken);
                        string? dieOutput = await dieTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!string.IsNullOrEmpty(dieOutput))
                        {
                            triageResult.DieAnalysis = dieOutput.Trim();
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (TimeoutException)
                    {
                        triageResult.Status = TriageStatus.PartialFailure;
                        triageResult.AnalyzerErrors["die"] = "DIE scan timed out after 5 seconds.";
                        triageResult.RiskFactors.Add("[DIE Timeout] Quá trình quét DIE vượt quá thời gian cho phép.");
                        triageResult.RiskLevel = "UNKNOWN";
                    }
                    catch (InvalidOperationException busyEx) when (busyEx.Message.Contains("busy"))
                    {
                        triageResult.Status = TriageStatus.PartialFailure;
                        triageResult.AnalyzerErrors["die"] = busyEx.Message;
                        triageResult.RiskFactors.Add($"[DIE Bận] {busyEx.Message}");
                        triageResult.RiskLevel = "UNKNOWN";
                    }
                    catch (Exception dieEx)
                    {
                        triageResult.Status = TriageStatus.PartialFailure;
                        triageResult.AnalyzerErrors["die"] = dieEx.Message;
                        triageResult.RiskFactors.Add($"[DIE Lỗi] Không thể quét chữ ký nhận dạng DIE: {dieEx.Message}");
                        triageResult.RiskLevel = "UNKNOWN";
                        System.Diagnostics.Debug.WriteLine($"[TriageService] DIE scan skipped or failed: {dieEx.Message}");
                    }
                }
                else
                {
                    triageResult.DieAnalysis = "Bỏ qua quét sâu DIE do tệp tin vượt quá 200 MiB.";
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 5. Entropy & Sections
                if (directResult.TryGetProperty("entropy", out var entropyObj))
                {
                    if (entropyObj.TryGetProperty("overall", out var overall))
                    {
                        triageResult.Entropy = overall.GetDouble();
                    }

                    if (entropyObj.TryGetProperty("sections", out var sectionsArr) &&
                        sectionsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sec in sectionsArr.EnumerateArray())
                        {
                            var secInfo = new SectionInfo
                            {
                                Name = sec.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                                Entropy = sec.TryGetProperty("entropy", out var ent) ? ent.GetDouble() : 0,
                                RawSize = sec.TryGetProperty("raw_size", out var rSize) ? rSize.GetInt64() : 0,
                                VirtualSize = sec.TryGetProperty("virtual_size", out var vSize) ? vSize.GetInt64() : 0,
                                Characteristics = sec.TryGetProperty("characteristics", out var ch)
                                    ? ch.GetString() ?? ""
                                    : "",
                                IsWritableAndExecutable =
                                    sec.TryGetProperty("is_writable_and_executable", out var wx) && wx.GetBoolean(),
                                IsFlagged = sec.TryGetProperty("is_flagged", out var flg) && flg.GetBoolean()
                            };

                            if (sec.TryGetProperty("anomalies", out var anomArr) &&
                                anomArr.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var a in anomArr.EnumerateArray())
                                {
                                    secInfo.Anomalies.Add(a.GetString() ?? "");
                                }
                            }

                            triageResult.Sections.Add(secInfo);
                        }
                    }

                    // Deep scan fallback (Binwalk native P/Invoke) on background thread
                    if (!triageResult.SizeTruncated &&
                        (triageResult.FileType == "unknown" || triageResult.FileType == "zip") &&
                        (triageResult.Packer == "unknown" || triageResult.Packer == "none"))
                    {
                        try
                        {
                            var bwTask = Task.Run(() =>
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                return _binwalkScanner(filePath);
                            }, cancellationToken);
                            string? binwalkJson = await bwTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (string.IsNullOrWhiteSpace(binwalkJson))
                            {
                                triageResult.Status = TriageStatus.PartialFailure;
                                triageResult.AnalyzerErrors["binwalk"] = "Binwalk scan returned empty output.";
                                triageResult.RiskFactors.Add("[Binwalk Lỗi] Quét sâu trả về kết quả rỗng.");
                                triageResult.RiskLevel = "UNKNOWN";
                            }
                            else
                            {
                                try
                                {
                                    var binwalkNode = JsonSerializer.Deserialize<JsonElement>(binwalkJson);
                                    if (binwalkNode.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var item in binwalkNode.EnumerateArray())
                                        {
                                            triageResult.DeepScanResults.Add(new BinwalkEntry
                                            {
                                                Offset = item.TryGetProperty("offset", out var off) ? off.ToString() : "0",
                                                Description = item.TryGetProperty("description", out var desc)
                                                    ? desc.GetString() ?? ""
                                                    : ""
                                            });
                                        }
                                    }
                                    else if (binwalkNode.ValueKind == JsonValueKind.Object)
                                    {
                                        JsonElement itemsArray = default;
                                        if (binwalkNode.TryGetProperty("file_map", out var fileMap) && fileMap.ValueKind == JsonValueKind.Array)
                                        {
                                            itemsArray = fileMap;
                                        }
                                        else if (binwalkNode.TryGetProperty("results", out var res) && res.ValueKind == JsonValueKind.Array)
                                        {
                                            itemsArray = res;
                                        }

                                        if (itemsArray.ValueKind == JsonValueKind.Array)
                                        {
                                            foreach (var item in itemsArray.EnumerateArray())
                                            {
                                                triageResult.DeepScanResults.Add(new BinwalkEntry
                                                {
                                                    Offset = item.TryGetProperty("offset", out var off) ? off.ToString() : "0",
                                                    Description = item.TryGetProperty("description", out var desc)
                                                        ? desc.GetString() ?? ""
                                                        : ""
                                                });
                                            }
                                        }
                                        else
                                        {
                                            triageResult.Status = TriageStatus.PartialFailure;
                                            triageResult.AnalyzerErrors["binwalk"] = "Binwalk JSON object missing 'file_map' array.";
                                            triageResult.RiskFactors.Add("[Binwalk Lỗi] Dữ liệu Binwalk thiếu trường 'file_map'.");
                                            triageResult.RiskLevel = "UNKNOWN";
                                        }
                                    }
                                    else
                                    {
                                        triageResult.Status = TriageStatus.PartialFailure;
                                        triageResult.AnalyzerErrors["binwalk"] = "Invalid Binwalk JSON payload (expected array or object).";
                                        triageResult.RiskFactors.Add("[Binwalk Lỗi] Cấu trúc JSON của Binwalk không hợp lệ.");
                                        triageResult.RiskLevel = "UNKNOWN";
                                    }
                                }
                                catch (JsonException jEx)
                                {
                                    triageResult.Status = TriageStatus.PartialFailure;
                                    triageResult.AnalyzerErrors["binwalk"] = $"Corrupted Binwalk JSON output: {jEx.Message}";
                                    triageResult.RiskFactors.Add($"[Binwalk Lỗi] Không thể giải mã JSON từ Binwalk: {jEx.Message}");
                                    triageResult.RiskLevel = "UNKNOWN";
                                }
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (TimeoutException)
                        {
                            triageResult.Status = TriageStatus.PartialFailure;
                            triageResult.AnalyzerErrors["binwalk"] = "Binwalk scan timed out after 5 seconds.";
                            triageResult.RiskFactors.Add("[Binwalk Timeout] Quá trình quét Binwalk vượt quá thời gian cho phép.");
                            triageResult.RiskLevel = "UNKNOWN";
                        }
                        catch (InvalidOperationException busyEx) when (busyEx.Message.Contains("busy"))
                        {
                            triageResult.Status = TriageStatus.PartialFailure;
                            triageResult.AnalyzerErrors["binwalk"] = busyEx.Message;
                            triageResult.RiskFactors.Add($"[Binwalk Bận] {busyEx.Message}");
                            triageResult.RiskLevel = "UNKNOWN";
                        }
                        catch (Exception bwEx)
                        {
                            triageResult.Status = TriageStatus.PartialFailure;
                            triageResult.AnalyzerErrors["binwalk"] = bwEx.Message;
                            triageResult.RiskFactors.Add($"[BINWALK Lỗi] Quét sâu thất bại: {bwEx.Message}");
                            triageResult.RiskLevel = "UNKNOWN";
                            System.Diagnostics.Debug.WriteLine($"[TriageService] Binwalk scan failed: {bwEx.Message}");
                        }
                    }
                    else if (entropyObj.TryGetProperty("deep_scan", out var deepScanArr) &&
                             deepScanArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in deepScanArr.EnumerateArray())
                        {
                            triageResult.DeepScanResults.Add(new BinwalkEntry
                            {
                                Offset = item.TryGetProperty("offset", out var off) ? off.ToString() : "0",
                                Description = item.TryGetProperty("description", out var desc)
                                    ? desc.GetString() ?? ""
                                    : ""
                            });
                        }
                    }
                }

                // 6. Imports Summary & Threat Categories
                if (directResult.TryGetProperty("imports_summary", out var importsSum))
                {
                    if (importsSum.TryGetProperty("total_imports", out var totalImp))
                    {
                        triageResult.TotalImportsCount = totalImp.GetInt32();
                    }

                    // Suspicious APIs
                    if (importsSum.TryGetProperty("all_suspicious", out var suspArr) &&
                        suspArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var imp in suspArr.EnumerateArray())
                        {
                            triageResult.SuspiciousImports.Add(imp.GetString() ?? "");
                        }
                    }
                    else if (importsSum.TryGetProperty("suspicious", out var legacySusp) &&
                             legacySusp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var imp in legacySusp.EnumerateArray())
                        {
                            triageResult.SuspiciousImports.Add(imp.GetString() ?? "");
                        }
                    }

                    // Categories
                    if (importsSum.TryGetProperty("categories", out var catObj) &&
                        catObj.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in catObj.EnumerateObject())
                        {
                            var list = new List<string>();
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var api in prop.Value.EnumerateArray())
                                {
                                    list.Add(api.GetString() ?? "");
                                }
                            }

                            triageResult.SuspiciousImportsByCategory[prop.Name] = list;
                        }
                    }

                    // Libraries
                    if (importsSum.TryGetProperty("libraries", out var libArr) &&
                        libArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var lib in libArr.EnumerateArray())
                        {
                            triageResult.Libraries.Add(lib.GetString() ?? "");
                        }
                    }
                }

                // 7. Risk Assessment
                if (directResult.TryGetProperty("risk_assessment", out var riskObj))
                {
                    if (riskObj.TryGetProperty("risk_score", out var rScore))
                    {
                        triageResult.RiskScore = rScore.GetInt32();
                    }

                    if (riskObj.TryGetProperty("risk_level", out var rLevel))
                    {
                        triageResult.RiskLevel = rLevel.GetString() ?? "UNKNOWN";
                    }

                    if (riskObj.TryGetProperty("risk_factors", out var rFactors) &&
                        rFactors.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var f in rFactors.EnumerateArray())
                        {
                            triageResult.RiskFactors.Add(f.GetString() ?? "");
                        }
                    }
                }

                // Invariant: If there are analyzer errors, partial failure, timeout, size truncated, or unsupported format (macho/unknown),
                // the risk level must NEVER be CLEAN and recommendation must reflect incomplete analysis.
                var supportedStaticTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "pe", "elf", "shared_object", "apk", "xapk", "apks", "dex"
                };

                if (triageResult.Status == TriageStatus.PartialFailure ||
                    triageResult.Status == TriageStatus.TimedOut ||
                    triageResult.AnalyzerErrors.Count > 0 ||
                    triageResult.SizeTruncated ||
                    hasSubprocessError ||
                    isTimeout ||
                    string.IsNullOrEmpty(triageResult.FileType) ||
                    !supportedStaticTypes.Contains(triageResult.FileType))
                {
                    triageResult.RiskLevel = "UNKNOWN";
                    triageResult.RecommendedPipeline = "manual.review";
                }

                if (string.Equals(triageResult.FileType, "macho", StringComparison.OrdinalIgnoreCase) &&
                    !triageResult.RiskFactors.Any(f => f.Contains("Mach-O")))
                {
                    triageResult.RiskFactors.Add("Định dạng Mach-O chưa có bộ phân tích cấu trúc chuyên sâu; kết quả chưa xác định (cần kiểm tra thủ công).");
                }

                if (triageResult.SizeTruncated && !triageResult.RiskFactors.Any(f => f.Contains("200 MiB")))
                {
                    triageResult.RiskFactors.Add("Tệp có dung lượng vượt quá giới hạn phân tích tĩnh (200 MiB); phân tích chỉ hoàn tất trên phần dữ liệu đầu.");
                }

                // Adjust risk score if binary is unsigned Windows PE
                if ((triageResult.Platform == "windows" || triageResult.FileType == "pe") &&
                    triageResult.Signature?.Status == "NotSigned" &&
                    triageResult.RiskScore > 0)
                {
                    triageResult.RiskFactors.Add("Tệp tin Windows PE không có chữ ký số (NotSigned)");
                }

                // 8. Android Metadata
                if (directResult.TryGetProperty("android_metadata", out var androidObj) &&
                    androidObj.ValueKind == JsonValueKind.Object)
                {
                    var meta = new AndroidMetadata
                    {
                        Format = androidObj.TryGetProperty("format", out var fmt) ? fmt.GetString() ?? "" : "",
                        PackageName = androidObj.TryGetProperty("package_name", out var pkg)
                            ? pkg.GetString() ?? ""
                            : "",
                        VersionCode = androidObj.TryGetProperty("version_code", out var vc) ? vc.GetString() ?? "" : "",
                        VersionName = androidObj.TryGetProperty("version_name", out var vn) ? vn.GetString() ?? "" : "",
                        MainActivity = androidObj.TryGetProperty("main_activity", out var ma)
                            ? ma.GetString() ?? ""
                            : "",
                        MinSdk = androidObj.TryGetProperty("min_sdk", out var minSdk) ? minSdk.GetString() : null,
                        TargetSdk = androidObj.TryGetProperty("target_sdk", out var tgtSdk) ? tgtSdk.GetString() : null,
                        PermissionsCount = androidObj.TryGetProperty("permissions_count", out var pCount)
                            ? pCount.GetInt32()
                            : 0,
                        DexCount = androidObj.TryGetProperty("dex_count", out var dexCount) ? dexCount.GetInt32() : 0,
                        Engine = androidObj.TryGetProperty("engine", out var eng)
                            ? eng.GetString() ?? "unknown"
                            : "unknown",
                        Packer = androidObj.TryGetProperty("packer", out var pkr) ? pkr.GetString() ?? "none" : "none",
                        IsSplitPackage = androidObj.TryGetProperty("is_split_package", out var isSplit) &&
                                         isSplit.GetBoolean()
                    };

                    if (androidObj.TryGetProperty("native_architectures", out var archArr) &&
                        archArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var a in archArr.EnumerateArray()) meta.NativeArchitectures.Add(a.GetString() ?? "");
                    }

                    if (androidObj.TryGetProperty("native_libraries", out var nLibArr) &&
                        nLibArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var nl in nLibArr.EnumerateArray()) meta.NativeLibraries.Add(nl.GetString() ?? "");
                    }

                    if (androidObj.TryGetProperty("dangerous_permissions", out var dpArr) &&
                        dpArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dp in dpArr.EnumerateArray())
                        {
                            meta.DangerousPermissions.Add(new AndroidDangerousPermission
                            {
                                Name = dp.TryGetProperty("name", out var dpName) ? dpName.GetString() ?? "" : "",
                                Risk = dp.TryGetProperty("risk", out var dpRisk) ? dpRisk.GetString() ?? "" : "",
                                Description = dp.TryGetProperty("description", out var dpDesc)
                                    ? dpDesc.GetString() ?? ""
                                    : ""
                            });
                        }
                    }

                    triageResult.AndroidMetadata = meta;
                }

                string riskBadge = triageResult.RiskLevel switch
                {
                    "CRITICAL" => $"🔴 **NGUY HIỂM CAO ({triageResult.RiskScore}/100)**",
                    "HIGH" => $"🟠 **RỦI RO CAO ({triageResult.RiskScore}/100)**",
                    "MEDIUM" => $"🟡 **TRUNG BÌNH ({triageResult.RiskScore}/100)**",
                    "LOW" => $"🟢 **THẤP ({triageResult.RiskScore}/100)**",
                    "CLEAN" => $"🛡️ **AN TOÀN ({triageResult.RiskScore}/100)**",
                    _ => $"❓ **CHƯA XÁC ĐỊNH ({triageResult.RiskScore}/100)**"
                };

                // Synchronize Markdown report with canonical C# hashes, Authenticode, risk level, DIE, Binwalk, and analyzer errors
                if (!string.IsNullOrEmpty(triageResult.MarkdownReport))
                {
                    // 1. Sync Risk Assessment Badge
                    triageResult.MarkdownReport = System.Text.RegularExpressions.Regex.Replace(
                        triageResult.MarkdownReport,
                        @"\*\*Đánh giá Rủi ro:\*\*.*",
                        $"**Đánh giá Rủi ro:** {riskBadge}"
                    );

                    // 1b. Sync Risk Factors list if factors exist
                    if (triageResult.RiskFactors.Count > 0)
                    {
                        var sbFactors = new System.Text.StringBuilder();
                        sbFactors.AppendLine("### 🚨 Yếu tố Rủi ro Phát hiện:");
                        foreach (var f in triageResult.RiskFactors)
                        {
                            sbFactors.AppendLine($"- ⚠️ {SanitizeMdText(f)}");
                        }

                        if (triageResult.MarkdownReport.Contains("### 🚨 Yếu tố Rủi ro Phát hiện:"))
                        {
                            triageResult.MarkdownReport = System.Text.RegularExpressions.Regex.Replace(
                                triageResult.MarkdownReport,
                                @"### 🚨 Yếu tố Rủi ro Phát hiện:(?:(?!## 1\.).)*",
                                sbFactors.ToString() + "\n",
                                System.Text.RegularExpressions.RegexOptions.Singleline
                            );
                        }
                        else
                        {
                            int insertFactorsIdx = triageResult.MarkdownReport.IndexOf("## 1. Thông Tin Tệp Tin");
                            if (insertFactorsIdx >= 0)
                            {
                                triageResult.MarkdownReport = triageResult.MarkdownReport.Insert(insertFactorsIdx, sbFactors.ToString() + "\n");
                            }
                        }
                    }

                    // 2. Sync Authenticode display
                    if (triageResult.Signature != null && triageResult.Platform == "windows")
                    {
                        string authDisplay = triageResult.Signature.Status switch
                        {
                            "Valid" => $"✅ **Hợp lệ** (`{SanitizeMdText(triageResult.Signature.Subject ?? "N/A")}`)",
                            "NotSigned" => "⚠️ **Không có chữ ký (NotSigned)**",
                            _ => $"❌ **Không hợp lệ ({SanitizeMdText(triageResult.Signature.Status)})**"
                        };
                        triageResult.MarkdownReport = triageResult.MarkdownReport
                            .Replace("⏳ *Đang chờ xác thực native C#...*", authDisplay);
                    }

                    // 3. Ensure canonical hashes are synchronized in the markdown table
                    if (triageResult.Hashes.TryGetValue("md5", out var canonicalMd5) && !string.IsNullOrEmpty(canonicalMd5))
                    {
                        if (triageResult.MarkdownReport.Contains("**MD5**"))
                        {
                            triageResult.MarkdownReport = System.Text.RegularExpressions.Regex.Replace(
                                triageResult.MarkdownReport,
                                @"\|\s*\*\*MD5\*\*\s*\|\s*`?[^|\r\n]+`?\s*\|",
                                $"| **MD5** | `{canonicalMd5}` |"
                            );
                        }
                        else
                        {
                            triageResult.MarkdownReport = System.Text.RegularExpressions.Regex.Replace(
                                triageResult.MarkdownReport,
                                @"(\|\s*\*\*Magic Bytes\*\*\s*\|\s*`?[^|\r\n]+`?\s*\|)",
                                $"$1\n| **MD5** | `{canonicalMd5}` |"
                            );
                        }
                    }
                    if (triageResult.Hashes.TryGetValue("sha256", out var canonicalSha) && !string.IsNullOrEmpty(canonicalSha))
                    {
                        if (triageResult.MarkdownReport.Contains("**SHA256**"))
                        {
                            triageResult.MarkdownReport = System.Text.RegularExpressions.Regex.Replace(
                                triageResult.MarkdownReport,
                                @"\|\s*\*\*SHA256\*\*\s*\|\s*`?[^|\r\n]+`?\s*\|",
                                $"| **SHA256** | `{canonicalSha}` |"
                            );
                        }
                        else
                        {
                            triageResult.MarkdownReport = System.Text.RegularExpressions.Regex.Replace(
                                triageResult.MarkdownReport,
                                @"(\|\s*\*\*MD5\*\*\s*\|\s*`?[^|\r\n]+`?\s*\|)",
                                $"$1\n| **SHA256** | `{canonicalSha}` |"
                            );
                        }
                    }

                    // 4. Ensure size truncation banner is present in report if file size > 200MB
                    if (triageResult.SizeTruncated && !triageResult.MarkdownReport.Contains("Lưu ý tệp dung lượng lớn"))
                    {
                        string truncationNotice = $"\n> ⚠️ **Lưu ý tệp dung lượng lớn (> 200 MiB):** Tệp tin có kích thước {triageResult.Size:N0} bytes. Mã băm SHA256/MD5 được tính toán trên toàn bộ tệp tin, trong khi phân tích cấu trúc section, Entropy và quét chuỗi/quy tắc bộ nhớ được giới hạn trên 200 MiB đầu tiên.\n";
                        int insertIdx = triageResult.MarkdownReport.IndexOf("## 1. Thông Tin Tệp Tin");
                        if (insertIdx >= 0)
                        {
                            triageResult.MarkdownReport = triageResult.MarkdownReport.Insert(insertIdx, truncationNotice);
                        }
                        else
                        {
                            triageResult.MarkdownReport = truncationNotice + triageResult.MarkdownReport;
                        }
                    }

                    // 5. If partial failure with analyzer errors, append alert banner if not already present
                    if (triageResult.Status == TriageStatus.PartialFailure && triageResult.AnalyzerErrors.Count > 0 &&
                        !triageResult.MarkdownReport.Contains("Cảnh báo lỗi phân tích"))
                    {
                        string errorBanner = $"\n> ⚠️ **Cảnh báo lỗi phân tích ({string.Join(", ", triageResult.AnalyzerErrors.Keys.Select(SanitizeMdText))}):** Một số bộ phân tích tĩnh gặp lỗi ({string.Join("; ", triageResult.AnalyzerErrors.Select(kv => $"{SanitizeMdText(kv.Key)}: {SanitizeMdText(kv.Value)}"))}). Kết quả chỉ phản ánh một phần tệp tin.\n";
                        int insertIdx = triageResult.MarkdownReport.IndexOf("## 1. Thông Tin Tệp Tin");
                        if (insertIdx >= 0)
                        {
                            triageResult.MarkdownReport = triageResult.MarkdownReport.Insert(insertIdx, errorBanner);
                        }
                    }

                    // 6. Sync DIE analysis if available
                    if (!string.IsNullOrEmpty(triageResult.DieAnalysis) && !triageResult.MarkdownReport.Contains("DIE Signatures"))
                    {
                        int dieHeaderIdx = triageResult.MarkdownReport.IndexOf("## 5. Nhận Diện Trình Biên Dịch & Packer (DIE / Engine)");
                        if (dieHeaderIdx >= 0)
                        {
                            int endSectionIdx = triageResult.MarkdownReport.IndexOf("## 7. ", dieHeaderIdx);
                            if (endSectionIdx < 0) endSectionIdx = triageResult.MarkdownReport.IndexOf("## 6. ", dieHeaderIdx);
                            if (endSectionIdx >= 0)
                            {
                                string dieSnippet = $"- **DIE Signatures:**\n```text\n{SanitizeMdBlock(triageResult.DieAnalysis)}\n```\n\n";
                                triageResult.MarkdownReport = triageResult.MarkdownReport.Insert(endSectionIdx, dieSnippet);
                            }
                            else
                            {
                                triageResult.MarkdownReport += $"\n- **DIE Signatures:**\n```text\n{SanitizeMdBlock(triageResult.DieAnalysis)}\n```\n";
                            }
                        }
                        else
                        {
                            int nextSecIdx = triageResult.MarkdownReport.IndexOf("## 7. Khuyến Nghị Hệ Thống");
                            if (nextSecIdx >= 0)
                            {
                                string dieSection = $"\n## 5. Nhận Diện Trình Biên Dịch & Packer (DIE / Engine)\n- **DIE Signatures:**\n```text\n{SanitizeMdBlock(triageResult.DieAnalysis)}\n```\n\n";
                                triageResult.MarkdownReport = triageResult.MarkdownReport.Insert(nextSecIdx, dieSection);
                            }
                            else
                            {
                                triageResult.MarkdownReport += $"\n## 5. Nhận Diện Trình Biên Dịch & Packer (DIE / Engine)\n- **DIE Signatures:**\n```text\n{SanitizeMdBlock(triageResult.DieAnalysis)}\n```\n";
                            }
                        }
                    }

                    // 7. Sync Binwalk deep scan if available
                    if (triageResult.DeepScanResults.Count > 0 && !triageResult.MarkdownReport.Contains("## 6. Tệp Nhúng Bên Trong"))
                    {
                        var sbBw = new System.Text.StringBuilder();
                        sbBw.AppendLine("\n## 6. Tệp Nhúng Bên Trong (Binwalk Deep Scan)");
                        foreach (var item in triageResult.DeepScanResults)
                        {
                            sbBw.AppendLine($"- `Offset: {SanitizeMdText(item.Offset)}`: {SanitizeMdText(item.Description)}");
                        }
                        int nextSecIdx = triageResult.MarkdownReport.IndexOf("## 7. Khuyến Nghị Hệ Thống");
                        if (nextSecIdx >= 0)
                        {
                            triageResult.MarkdownReport = triageResult.MarkdownReport.Insert(nextSecIdx, sbBw.ToString() + "\n");
                        }
                        else
                        {
                            triageResult.MarkdownReport += sbBw.ToString();
                        }
                    }

                    // 8. Override action hint and next pipeline if risk is UNKNOWN or partial failure
                    if (triageResult.RiskLevel == "UNKNOWN" || triageResult.Status == TriageStatus.PartialFailure || triageResult.AnalyzerErrors.Count > 0)
                    {
                        triageResult.MarkdownReport = triageResult.MarkdownReport.Replace(
                            "Tệp sạch, cấu trúc rõ ràng. Đẩy thẳng vào Ghidra Headless MCP hoặc IDA Pro để xuất mã giả C++.",
                            "Tệp thực thi Windows (PE) tiêu chuẩn. Sẵn sàng cho phân tích sâu bằng Ghidra hoặc IDA Pro."
                        );

                        if (triageResult.RecommendedPipeline == "manual.review")
                        {
                            triageResult.MarkdownReport = System.Text.RegularExpressions.Regex.Replace(
                                triageResult.MarkdownReport,
                                @"👉\s*\*\*Định tuyến tiếp theo:\*\*\s*`?[^`\r\n]+`?",
                                "👉 **Định tuyến tiếp theo:** `manual.review`"
                            );
                        }

                        if (triageResult.AnalyzerErrors.Count > 0 || triageResult.RiskLevel == "UNKNOWN")
                        {
                            triageResult.MarkdownReport = System.Text.RegularExpressions.Regex.Replace(
                                triageResult.MarkdownReport,
                                @">\s*\*Hành động đề xuất:\*.*",
                                "> *Hành động đề xuất:* ⚠️ Phân tích chưa hoàn tất hoặc gặp lỗi analyzer. Cần kiểm tra thủ công."
                            );
                        }
                    }
                }
                else
                {
                    // Synthesize fallback markdown report
                    var sbFallback = new System.Text.StringBuilder();
                    sbFallback.AppendLine("# 🛡️ Báo Cáo Phân Tích Tĩnh — Executive Triage");
                    sbFallback.AppendLine($"**Tệp tin:** `{SanitizeMdText(triageResult.FilePath)}`  ");
                    sbFallback.AppendLine($"**Đánh giá Rủi ro:** {riskBadge}");
                    if (triageResult.RiskFactors.Count > 0)
                    {
                        sbFallback.AppendLine("\n### 🚨 Yếu tố Rủi ro Phát hiện:");
                        foreach (var f in triageResult.RiskFactors)
                        {
                            sbFallback.AppendLine($"- ⚠️ {SanitizeMdText(f)}");
                        }
                    }
                    if (triageResult.AnalyzerErrors.Count > 0)
                    {
                        sbFallback.AppendLine($"\n> ⚠️ **Cảnh báo lỗi phân tích ({string.Join(", ", triageResult.AnalyzerErrors.Keys.Select(SanitizeMdText))}):** Một số bộ phân tích tĩnh gặp lỗi.");
                    }
                    if (triageResult.SizeTruncated)
                    {
                        sbFallback.AppendLine($"\n> ⚠️ **Lưu ý tệp dung lượng lớn (> 200 MiB):** Tệp tin có kích thước {triageResult.Size:N0} bytes. Mã băm SHA256/MD5 được tính toán trên toàn bộ tệp tin, trong khi phân tích cấu trúc section, Entropy và quét chuỗi/quy tắc bộ nhớ được giới hạn trên 200 MiB đầu tiên.");
                    }
                    sbFallback.AppendLine("\n## 1. Thông Tin Tệp Tin & Mã Băm");
                    sbFallback.AppendLine("| Thuộc tính | Giá trị |");
                    sbFallback.AppendLine("|---|---|");
                    sbFallback.AppendLine($"| **Định dạng / Nền tảng** | `{(string.IsNullOrEmpty(triageResult.FileType) ? "Unknown" : SanitizeMdText(triageResult.FileType.ToUpperInvariant()))}` / `{(string.IsNullOrEmpty(triageResult.Platform) ? "Unknown" : SanitizeMdText(triageResult.Platform))}` |");
                    sbFallback.AppendLine($"| **Kích thước** | {triageResult.Size:N0} bytes |");
                    if (triageResult.Hashes.TryGetValue("md5", out var md5Val) && !string.IsNullOrEmpty(md5Val))
                        sbFallback.AppendLine($"| **MD5** | `{SanitizeMdText(md5Val)}` |");
                    if (triageResult.Hashes.TryGetValue("sha256", out var shaVal) && !string.IsNullOrEmpty(shaVal))
                        sbFallback.AppendLine($"| **SHA256** | `{SanitizeMdText(shaVal)}` |");
                    sbFallback.AppendLine("\n## 7. Khuyến Nghị Hệ Thống (Next Pipeline)");
                    sbFallback.AppendLine($"👉 **Định tuyến tiếp theo:** `{SanitizeMdText(triageResult.RecommendedPipeline)}`");
                    triageResult.MarkdownReport = sbFallback.ToString();
                }

                return triageResult;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                triageResult.Status = TriageStatus.PartialFailure;
                triageResult.RecommendedPipeline = "error.triage_failed";
                triageResult.RiskLevel = "UNKNOWN";
                triageResult.RiskFactors.Add($"Triage thất bại: {ex.Message}");
                triageResult.MarkdownReport = $"# 🛡️ Báo Cáo Phân Tích Tĩnh — Executive Triage\n**Tệp tin:** `{SanitizeMdText(filePath)}`  \n**Đánh giá Rủi ro:** ❓ **CHƯA XÁC ĐỊNH (0/100)**\n\n### 🚨 Yếu tố Rủi ro Phát hiện:\n- ⚠️ Triage thất bại: {SanitizeMdText(ex.Message)}\n\n> ❌ **Lỗi phân tích:** Quá trình phân tích tĩnh gặp ngoại lệ nghiêm trọng: {SanitizeMdText(ex.Message)}";
                System.Diagnostics.Debug.WriteLine($"[TriageService] Error: {ex}");
                return triageResult;
            }
        }

        private static string SanitizeMdText(string? val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            return val.Replace("\r", " ").Replace("\n", " ").Replace("`", "'").Replace("|", "/").Trim();
        }

        private static string SanitizeMdBlock(string? val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            return val.Replace("```", "'''");
        }
    }
}