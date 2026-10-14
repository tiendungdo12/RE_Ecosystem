using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RE.Core.Abstractions;
using RE.Core.Services;
using RE.Core.StaticAnalysis;
using RE.Domain.Models;
using RE.Platform.Windows;
using Xunit;

namespace App_Test;

public class DeterministicTriageTests
{
    public DeterministicTriageTests()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
#pragma warning disable CS0618
        TriageOrchestrator.Configure(new TriageService(verifier, env));
#pragma warning restore CS0618
    }

    private static string GetEcosystemRoot()
    {
        string? currentDir = AppDomain.CurrentDomain.BaseDirectory;
        while (currentDir != null)
        {
            if (Directory.Exists(Path.Combine(currentDir, "Python_Runtime")))
            {
                return currentDir;
            }
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }
        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\"));
    }

    [Fact]
    public async Task PeTriage_CanonicalPath_SucceedsWithoutMcpOrLlm()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");
        Assert.True(File.Exists(targetFile), $"Test file not found: {targetFile}");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal("pe", result.FileType);
        Assert.Equal("windows", result.Platform);
        Assert.True(result.Size > 0);
        Assert.NotEmpty(result.Hashes["md5"]);
        Assert.NotEmpty(result.Hashes["sha256"]);
        Assert.NotEqual("NotChecked", result.Signature.Status);
        Assert.True(result.Sections.Count > 0);
        Assert.NotEqual("error.triage_failed", result.RecommendedPipeline);
        Assert.True(result.Status == TriageStatus.Completed || result.Status == TriageStatus.PartialFailure);
    }

    [Fact]
    public async Task MissingFile_SetsUnknownRiskAndErrorPipeline()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env);

        var result = await pipeline.RunTriageAsync(@"D:\non_existent_file_12345.exe");

        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.Equal("error.file_not_found", result.RecommendedPipeline);
    }

    [Fact]
    public async Task Cancellation_BeforeTriage_ThrowsOperationCanceledException()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await pipeline.RunTriageAsync(targetFile, cts.Token);
        });
    }

    [Fact]
    public async Task Cancellation_DuringTriage_PropagatesCancellationWithoutSwallowing()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();

        // Custom analyzer that simulates work and triggers cancellation mid-execution
        var mockAnalyzer = new MidExecutionCancellingAnalyzer();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env, mockAnalyzer);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        using var cts = new CancellationTokenSource();
        mockAnalyzer.TokenSourceToCancel = cts;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await pipeline.RunTriageAsync(targetFile, cts.Token);
        });
    }

    [Fact]
    public async Task SubprocessError_IsolatesFailure_SetsUnknownNotClean()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();

        // Custom analyzer that simulates an analyzer failure (e.g. corrupt header)
        var errorAnalyzer = new ErrorReturningSubprocessAnalyzer("LIEF parse failed: corrupted section table");
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env, errorAnalyzer);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.NotNull(result.SubprocessAnalyzerError);
        Assert.Contains(result.RiskFactors, f => f.Contains("corrupted section table"));
    }

    [Fact]
    public async Task SubprocessTimeout_SetsTimedOutStatus_SetsUnknownNotClean()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();

        var timeoutAnalyzer = new TimeoutReturningSubprocessAnalyzer();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env, timeoutAnalyzer);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.Equal(TriageStatus.TimedOut, result.Status);
        Assert.True(result.IsTimeout);
        Assert.NotNull(result.SubprocessAnalyzerError);
        Assert.Contains("[TIMEOUT]", result.RiskFactors[0]);
    }

    [Fact]
    public async Task AntiRegression_McpStaticTriageRoute_IsBlocked()
    {
        var env = new WindowsEnvironment();
        var mcpManager = new McpClientManager(env);

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await mcpManager.GetClientAsync("static.triage");
        });
    }

    [Fact]
    public async Task TriageOrchestrator_DelegatesToPipeline_WithAuthenticode()
    {
        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

#pragma warning disable CS0618 // Type or member is obsolete
        string rawJson = await TriageOrchestrator.RunTriageAsync(targetFile);
#pragma warning restore CS0618

        Assert.NotEmpty(rawJson);
        using var doc = JsonDocument.Parse(rawJson);
        var rootElem = doc.RootElement;

        Assert.Equal("pe", rootElem.GetProperty("FileType").GetString());
        Assert.Equal("windows", rootElem.GetProperty("Platform").GetString());
        Assert.NotEqual("NotChecked", rootElem.GetProperty("Signature").GetProperty("Status").GetString());
    }

    [Fact]
    public async Task LargeFile_TruncatedFlag_CalculatesSizeAccurately()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();

        var truncatedAnalyzer = new TruncatedReturningSubprocessAnalyzer();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env, truncatedAnalyzer);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.True(result.SizeTruncated);
        Assert.True(result.Size > 0);
        Assert.Contains("Lưu ý tệp dung lượng lớn", result.MarkdownReport);
        Assert.DoesNotContain("⏳ *Đang chờ xác thực native C#...*", result.MarkdownReport);
    }

    [Fact]
    public async Task RunTriageAsync_WhenFileIsLargerThan200MB_SkipsDieAndBinwalkScanners()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();
        var truncatedAnalyzer = new TruncatedReturningSubprocessAnalyzer();

        bool dieCalled = false;
        Func<string, string, string?> spyDieScanner = (filePath, dbPath) =>
        {
            dieCalled = true;
            return "PE64_Spy";
        };

        bool binwalkCalled = false;
        Func<string, string?> spyBinwalkScanner = filePath =>
        {
            binwalkCalled = true;
            return "{\"file_map\":[]}";
        };

        ITriagePipeline pipeline = new TriageService(
            signatureVerifier,
            env,
            subprocessAnalyzer: truncatedAnalyzer,
            binwalkScanner: spyBinwalkScanner,
            dieScanner: spyDieScanner);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        // Assert DIE and Binwalk native scanners were skipped
        Assert.False(dieCalled, "DIE scanner must not be invoked when SizeTruncated is true.");
        Assert.False(binwalkCalled, "Binwalk scanner must not be invoked when SizeTruncated is true.");

        // Assert skip message is recorded in DieAnalysis
        Assert.NotNull(result.DieAnalysis);
        Assert.Contains("Bỏ qua quét sâu DIE do tệp tin vượt quá 200 MiB", result.DieAnalysis);

        // Assert result state reflects truncation
        Assert.True(result.SizeTruncated);
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.Equal("manual.review", result.RecommendedPipeline);
        Assert.Contains("Lưu ý tệp dung lượng lớn", result.MarkdownReport);
    }

    [Fact]
    public async Task Cancellation_RealSubprocess_ThrowsOperationCanceledException()
    {
        var env = new WindowsEnvironment();
        var realAnalyzer = new DirectTriageSubprocessAnalyzer(env);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        using var cts = new CancellationTokenSource();
        int startedPid = 0;
        realAnalyzer.OnProcessStarted = pid =>
        {
            startedPid = pid;
            cts.Cancel();
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await realAnalyzer.AnalyzeAsync(targetFile, cts.Token);
        });
        sw.Stop();

        Assert.True(startedPid > 0, "Subprocess must have actually started before cancellation.");
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Cancellation should be prompt, took {sw.ElapsedMilliseconds}ms");

        await Task.Delay(100);
        bool isDead = false;
        try
        {
            var proc = System.Diagnostics.Process.GetProcessById(startedPid);
            isDead = proc.HasExited;
        }
        catch (ArgumentException)
        {
            isDead = true;
        }
        Assert.True(isDead, "Subprocess must be terminated promptly upon cancellation.");
    }

    [Fact]
    public async Task RealLargeFile_Over200MB_SetsSizeTruncatedAndAccurateHashes()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env);

        string tempLargeFile = Path.Combine(Path.GetTempPath(), $"large_test_{Guid.NewGuid():N}.bin");
        try
        {
            // Create a 205 MiB file instantly without disk bloat
            const long TargetSize = 205L * 1024 * 1024;
            using (var fs = new FileStream(tempLargeFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.SetLength(TargetSize);
            }

            var result = await pipeline.RunTriageAsync(tempLargeFile);

            string expectedSha256;
            string expectedMd5;
            using (var fs = File.OpenRead(tempLargeFile))
            {
                expectedSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(fs));
            }
            using (var fs = File.OpenRead(tempLargeFile))
            {
                expectedMd5 = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(fs));
            }

            Assert.True(result.SizeTruncated);
            Assert.Equal(TargetSize, result.Size);
            Assert.Equal(expectedSha256, result.Hashes["sha256"]);
            Assert.Equal(expectedMd5, result.Hashes["md5"]);
            Assert.Contains("Lưu ý tệp dung lượng lớn (> 200 MiB)", result.MarkdownReport);
            Assert.Contains($"{TargetSize:N0} bytes", result.MarkdownReport);
            Assert.Contains(expectedSha256, result.MarkdownReport);
            Assert.Equal("UNKNOWN", result.RiskLevel);
        }
        finally
        {
            if (File.Exists(tempLargeFile))
            {
                File.Delete(tempLargeFile);
            }
        }
    }

    [Fact]
    public async Task RealUnknownBinary_Triage_SetsUnknownRisk_NeverClean()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env);

        string tempFile = Path.Combine(Path.GetTempPath(), $"unknown_test_{Guid.NewGuid():N}.bin");
        try
        {
            // Write arbitrary non-PE, non-ELF, non-APK binary bytes
            byte[] bytes = new byte[2048];
            new Random(42).NextBytes(bytes);
            await File.WriteAllBytesAsync(tempFile, bytes);

            var result = await pipeline.RunTriageAsync(tempFile);

            Assert.Equal("unknown", result.FileType);
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task CorruptedApk_SetsAnalyzerError_AndUnknownRisk_NeverClean()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"corrupted_{Guid.NewGuid():N}.apk");
        try
        {
            await File.WriteAllBytesAsync(tempApk, System.Text.Encoding.UTF8.GetBytes("THIS IS NOT A VALID ZIP ARCHIVE"));
            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal("apk", result.FileType);
            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for corrupted APK");
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.False(result.MarkdownReport.Contains("AN TOÀN"), "Report must not claim corrupted APK is AN TOAN");
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    [Fact]
    public async Task UnknownRiskReport_HasUnknownBadge_NeverSafeBadge()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env);

        string tempFile = Path.Combine(Path.GetTempPath(), $"unknown_{Guid.NewGuid():N}.bin");
        try
        {
            byte[] bytes = new byte[1024];
            new Random(123).NextBytes(bytes);
            await File.WriteAllBytesAsync(tempFile, bytes);

            var result = await pipeline.RunTriageAsync(tempFile);

            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task NativeDieFailure_IsIsolated_SetsPartialFailureAndUnknownRisk()
    {
        var env = new CustomDieDbEnvironment(@"C:\Invalid\DieDb\Path_DoesNotExist_123");
        var signatureVerifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.True(result.AnalyzerErrors.ContainsKey("die"), "Expected 'die' analyzer error");
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.NotEqual("CLEAN", result.RiskLevel);
        Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
        Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
        // Authenticode and sections are still preserved
        Assert.NotNull(result.Signature);
        Assert.True(result.Sections.Count > 0);
    }

    [Fact]
    public async Task NativeDieNullPointer_IsIsolated_SetsPartialFailureAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(
            signatureVerifier,
            env,
            dieScanner: (_, _) => throw new InvalidOperationException("DIE native scan returned null pointer for target file."));

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.True(result.AnalyzerErrors.ContainsKey("die"), "Expected 'die' analyzer error for null pointer");
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.Contains(result.RiskFactors, rf => rf.Contains("DIE native scan returned null pointer"));
        Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
    }

    [Fact]
    public async Task AuthenticodeVerifier_CatalogAndEmbedded_ParityWithTriageOrchestrator()
    {
        string root = GetEcosystemRoot();
        string pythonExe = Path.Combine(root, @"Python_Runtime\python.exe");

        var verifier = new AuthenticodeVerifier();

        // 1. Embedded signature test fixture (python.exe)
        var embeddedResult = verifier.Verify(pythonExe);
        Assert.Equal("Valid", embeddedResult.Status);
        Assert.NotNull(embeddedResult.Subject);
        Assert.Contains("Python Software Foundation", embeddedResult.Subject);

#pragma warning disable CS0618
        string orchJson = await TriageOrchestrator.RunTriageAsync(pythonExe);
#pragma warning restore CS0618

        using var doc = JsonDocument.Parse(orchJson);
        string orchStatus = doc.RootElement.GetProperty("Signature").GetProperty("Status").GetString() ?? "";
        Assert.Equal("Valid", orchStatus);

        // 2. Catalog-only signature test fixture (Windows system binary e.g. cmd.exe)
        string systemCmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (File.Exists(systemCmd))
        {
            // Verify cmd.exe has NO embedded Authenticode certificate (catalog signed only)
            bool hasEmbeddedCert = false;
            try
            {
                var certType = System.Security.Cryptography.X509Certificates.X509Certificate2.GetCertContentType(systemCmd);
                hasEmbeddedCert = (certType == System.Security.Cryptography.X509Certificates.X509ContentType.Authenticode);
            }
            catch
            {
                hasEmbeddedCert = false;
            }
            Assert.False(hasEmbeddedCert, "cmd.exe must not have an embedded certificate; it must be catalog-signed.");

            // Catalog verification succeeds independently
            var cmdSig = verifier.Verify(systemCmd);
            Assert.Equal("Valid", cmdSig.Status);
            // Subject is null for catalog signatures because the cert resides in the catalog DB
            Assert.Null(cmdSig.Subject);

#pragma warning disable CS0618
            string cmdOrchJson = await TriageOrchestrator.RunTriageAsync(systemCmd);
#pragma warning restore CS0618
            using var cmdDoc = JsonDocument.Parse(cmdOrchJson);
            string cmdOrchStatus = cmdDoc.RootElement.GetProperty("Signature").GetProperty("Status").GetString() ?? "";
            Assert.Equal("Valid", cmdOrchStatus);
        }

        // 3. Unsigned binary test fixtures
        // 3a. Non-PE arbitrary binary returns NotApplicable
        string tempUnsigned = Path.Combine(Path.GetTempPath(), $"unsigned_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(tempUnsigned, new byte[1024]);
            var unsignedSig = verifier.Verify(tempUnsigned);
            Assert.Equal("NotApplicable", unsignedSig.Status);
            Assert.Null(unsignedSig.Subject);
        }
        finally
        {
            if (File.Exists(tempUnsigned)) File.Delete(tempUnsigned);
        }

        // 3b. Genuine unsigned PE binary returns NotSigned
        string appTestExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Test.exe");
        if (File.Exists(appTestExe))
        {
            var unsignedPeSig = verifier.Verify(appTestExe);
            Assert.Equal("NotSigned", unsignedPeSig.Status);
            Assert.Null(unsignedPeSig.Subject);
        }
    }

    [Fact]
    public async Task NativeTimeout_IsIsolatedPromptlyAndSetsPartialFailureAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();

        int invocationCount = 0;
        object testScanLock = new object();

        // Simulate a native scan with scan-level serialization lock (matching DieWrapper._scanLock)
        var slowDieScanner = new Func<string, string, string?>((filePath, dbPath) =>
        {
            int count = Interlocked.Increment(ref invocationCount);
            bool acquired = false;
            try
            {
                Monitor.TryEnter(testScanLock, TimeSpan.Zero, ref acquired);
                if (!acquired)
                {
                    throw new InvalidOperationException("DIE scanner is busy executing a prior operation.");
                }

                if (count == 1)
                {
                    // First call: slow native execution (8.0s > 5.0s timeout)
                    Thread.Sleep(8000);
                    return "PE64_Slow";
                }
                return "PE64_Normal";
            }
            finally
            {
                if (acquired)
                {
                    Monitor.Exit(testScanLock);
                }
            }
        });

        ITriagePipeline pipeline = new TriageService(
            verifier,
            env,
            dieScanner: slowDieScanner);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        // 1. First invocation: Caller unblocks promptly after 5s timeout instead of hanging 8.0s
        var sw = Stopwatch.StartNew();
        var result1 = await pipeline.RunTriageAsync(targetFile);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 7500, $"Triage should time out after ~5s, took {sw.ElapsedMilliseconds}ms");
        Assert.Equal(TriageStatus.PartialFailure, result1.Status);
        Assert.True(result1.AnalyzerErrors.ContainsKey("die"), "Expected 'die' analyzer error due to timeout.");
        Assert.Contains("timed out", result1.AnalyzerErrors["die"]);
        Assert.Equal("UNKNOWN", result1.RiskLevel);
        Assert.Equal("manual.review", result1.RecommendedPipeline);
        Assert.Contains("Cảnh báo lỗi phân tích (die)", result1.MarkdownReport);
        Assert.DoesNotContain("AN TOÀN", result1.MarkdownReport);
        Assert.Contains("manual.review", result1.MarkdownReport);

        // Non-timed-out analyzers (Authenticode, hashes) are intact
        Assert.NotNull(result1.Signature);
        Assert.Equal("Valid", result1.Signature.Status);
        Assert.NotEmpty(result1.Hashes["sha256"]);

        // 2. Second invocation immediately after timeout while worker 1 is STILL running in unmanaged code (~5.0s < 8.0s)
        // Crucial concurrency protection test: Worker 2 does NOT enter overlapping native execution.
        // It is safely serialized and isolated with 'busy' status without crashing or corrupting unmanaged state!
        var sw2 = Stopwatch.StartNew();
        var result2 = await pipeline.RunTriageAsync(targetFile);
        sw2.Stop();

        Assert.True(sw2.ElapsedMilliseconds < 3000, $"Second invocation should return promptly with busy isolation, took {sw2.ElapsedMilliseconds}ms");
        Assert.Equal(TriageStatus.PartialFailure, result2.Status);
        Assert.True(result2.AnalyzerErrors.ContainsKey("die"));
        Assert.Contains("busy", result2.AnalyzerErrors["die"]);
        Assert.Equal("UNKNOWN", result2.RiskLevel);
        Assert.Contains(result2.RiskFactors, rf => rf.Contains("DIE Bận"));

        // 3. Third invocation after worker 1 has finished (wait 3.5s for worker 1's 8.0s sleep to complete)
        // Clean recovery test: Once the prior native worker exits and releases its scan lock, subsequent scans succeed cleanly.
        await Task.Delay(3500);
        var sw3 = Stopwatch.StartNew();
        var result3 = await pipeline.RunTriageAsync(targetFile);
        sw3.Stop();

        Assert.True(sw3.ElapsedMilliseconds < 3000, $"Third invocation should execute promptly, took {sw3.ElapsedMilliseconds}ms");
        Assert.Equal(TriageStatus.Completed, result3.Status);
        Assert.False(result3.AnalyzerErrors.ContainsKey("die"));
        Assert.Equal("PE64_Normal", result3.DieAnalysis);
        Assert.NotNull(result3.Signature);
        Assert.Equal("Valid", result3.Signature.Status);
    }

    [Fact]
    public async Task NativeDieInitializationTimeout_IsIsolatedPromptlyAndSetsPartialFailureAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();

        int dbLoadInvocationCount = 0;
        int scanInvocationCount = 0;
        var dbLoadStartedSignal = new ManualResetEventSlim(false);

        DieWrapper.ResetForTesting();
        try
        {
            DieWrapper.LoadDatabaseOverride = dbPath =>
            {
                int call = Interlocked.Increment(ref dbLoadInvocationCount);
                dbLoadStartedSignal.Set();
                if (call == 1)
                {
                    // Worker #1: Simulate slow database initialization (8.0s > 5.0s timeout in TriageService)
                    Thread.Sleep(8000);
                    return 1; // success
                }
                return 1;
            };

            DieWrapper.ScanFileOverride = (filePath, flags, dbPath) =>
            {
                Interlocked.Increment(ref scanInvocationCount);
                return System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("PE32_InitializedSuccessfully");
            };

            // Use default TriageService constructor which internally invokes DieWrapper.Scan
            ITriagePipeline pipeline = new TriageService(verifier, env);

            string root = GetEcosystemRoot();
            string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

            // 1. First invocation: Caller unblocks promptly after 5.0s timeout instead of hanging 8.0s
            var sw1 = Stopwatch.StartNew();
            var result1 = await pipeline.RunTriageAsync(targetFile);
            sw1.Stop();

            Assert.True(sw1.ElapsedMilliseconds < 7500, $"Triage should time out after ~5s, took {sw1.ElapsedMilliseconds}ms");
            Assert.Equal(TriageStatus.PartialFailure, result1.Status);
            Assert.True(result1.AnalyzerErrors.ContainsKey("die"), "Expected 'die' analyzer error due to timeout.");
            Assert.Contains("timed out", result1.AnalyzerErrors["die"]);
            Assert.Equal("UNKNOWN", result1.RiskLevel);
            Assert.Equal("manual.review", result1.RecommendedPipeline);
            Assert.Contains("Cảnh báo lỗi phân tích (die)", result1.MarkdownReport);

            // Crucial assertion 1: Worker #1 was actively performing database load and continues past caller timeout
            Assert.True(dbLoadStartedSignal.IsSet, "Database load was initiated by Worker #1.");
            Assert.Equal(1, dbLoadInvocationCount);

            // 2. Second invocation immediately after timeout while Worker #1 is STILL initializing in background
            // Concurrency protection: Worker #2 does NOT block on _dbInitLock, does NOT overlap native init.
            // It is rejected at Level 1 (_scanLock) and fails fast with BUSY!
            var sw2 = Stopwatch.StartNew();
            var result2 = await pipeline.RunTriageAsync(targetFile);
            sw2.Stop();

            Assert.True(sw2.ElapsedMilliseconds < 3000, $"Second invocation should return promptly with busy isolation, took {sw2.ElapsedMilliseconds}ms");
            Assert.Equal(TriageStatus.PartialFailure, result2.Status);
            Assert.True(result2.AnalyzerErrors.ContainsKey("die"));
            Assert.Contains("busy", result2.AnalyzerErrors["die"]);
            Assert.Equal("UNKNOWN", result2.RiskLevel);
            Assert.Contains(result2.RiskFactors, rf => rf.Contains("DIE Bận"));

            // Crucial assertion 2: Worker #2 NEVER invoked database load or scan (no overlap)
            Assert.Equal(1, dbLoadInvocationCount);
            Assert.Equal(0, scanInvocationCount);

            // 3. Wait for Worker #1 to finish its 8.0s background initialization (~3.5s remaining)
            await Task.Delay(3500);

            // 4. Third invocation: Clean recovery once Worker #1 finished initialization
            var sw3 = Stopwatch.StartNew();
            var result3 = await pipeline.RunTriageAsync(targetFile);
            sw3.Stop();

            Assert.True(sw3.ElapsedMilliseconds < 3000, $"Third invocation should execute promptly, took {sw3.ElapsedMilliseconds}ms");
            Assert.Equal(TriageStatus.Completed, result3.Status);
            Assert.False(result3.AnalyzerErrors.ContainsKey("die"));
            Assert.Equal("PE32_InitializedSuccessfully", result3.DieAnalysis);
            Assert.NotNull(result3.Signature);
            Assert.Equal("Valid", result3.Signature.Status);

            // Crucial assertion 3: Database was NOT re-initialized because _databaseLoaded was true
            Assert.Equal(1, dbLoadInvocationCount);
            // Worker #1 finished its scan in background (1) + Worker #3 finished its scan (1) = 2 total scans
            Assert.Equal(2, scanInvocationCount);
        }
        finally
        {
            DieWrapper.ResetForTesting();
        }
    }

    [Fact]
    public async Task NativeDieInitializationCancellation_IsIsolatedPromptlyAndSetsBusyForSubsequentCalls()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();

        int dbLoadInvocationCount = 0;
        int scanInvocationCount = 0;
        var dbLoadStartedSignal = new ManualResetEventSlim(false);

        DieWrapper.ResetForTesting();
        try
        {
            DieWrapper.LoadDatabaseOverride = dbPath =>
            {
                int call = Interlocked.Increment(ref dbLoadInvocationCount);
                dbLoadStartedSignal.Set();
                if (call == 1)
                {
                    // Worker #1: Simulate slow database initialization (4.0s)
                    Thread.Sleep(4000);
                    return 1;
                }
                return 1;
            };

            DieWrapper.ScanFileOverride = (filePath, flags, dbPath) =>
            {
                Interlocked.Increment(ref scanInvocationCount);
                return System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("PE32_InitializedSuccessfully");
            };

            ITriagePipeline pipeline = new TriageService(verifier, env);

            string root = GetEcosystemRoot();
            string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

            using var cts = new CancellationTokenSource();

            // 1. First invocation: start triage task with cancellation token
            var triageTask = pipeline.RunTriageAsync(targetFile, cts.Token);

            // Wait until Worker #1 is actively running inside LoadDatabaseOverride
            bool started = dbLoadStartedSignal.Wait(5000);
            Assert.True(started, "Worker #1 must have entered database initialization before cancellation.");

            // Cancel token while Worker #1 is actively in unmanaged execution
            cts.Cancel();

            // Caller unblocks promptly with OperationCanceledException
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await triageTask);

            // Crucial assertion 1: Worker #1 was actively performing database load and continues past caller cancellation
            Assert.True(dbLoadStartedSignal.IsSet, "Database load was initiated by Worker #1 before cancellation.");
            Assert.Equal(1, dbLoadInvocationCount);

            // 2. Second invocation: immediately called while Worker #1 is STILL running in unmanaged/sleep
            // Concurrency protection: Worker #2 does NOT block on _dbInitLock, does NOT overlap native init.
            // It is rejected at Level 1 (_scanLock) and fails fast with BUSY!
            var sw2 = Stopwatch.StartNew();
            var result2 = await pipeline.RunTriageAsync(targetFile);
            sw2.Stop();

            Assert.True(sw2.ElapsedMilliseconds < 3000, $"Second invocation should return promptly with busy isolation, took {sw2.ElapsedMilliseconds}ms");
            Assert.Equal(TriageStatus.PartialFailure, result2.Status);
            Assert.True(result2.AnalyzerErrors.ContainsKey("die"));
            Assert.Contains("busy", result2.AnalyzerErrors["die"]);
            Assert.Equal("UNKNOWN", result2.RiskLevel);
            Assert.Contains(result2.RiskFactors, rf => rf.Contains("DIE Bận"));

            // Crucial assertion 2: Worker #2 NEVER invoked database load or scan (no overlap)
            Assert.Equal(1, dbLoadInvocationCount);
            Assert.Equal(0, scanInvocationCount);

            // 3. Wait for Worker #1 to complete its background initialization and scan (~2.5s remaining)
            await Task.Delay(3500);

            // 4. Third invocation: Clean recovery once Worker #1 finished initialization
            var sw3 = Stopwatch.StartNew();
            var result3 = await pipeline.RunTriageAsync(targetFile);
            sw3.Stop();

            Assert.True(sw3.ElapsedMilliseconds < 3000, $"Third invocation should execute promptly, took {sw3.ElapsedMilliseconds}ms");
            Assert.Equal(TriageStatus.Completed, result3.Status);
            Assert.False(result3.AnalyzerErrors.ContainsKey("die"));
            Assert.Equal("PE32_InitializedSuccessfully", result3.DieAnalysis);
            Assert.NotNull(result3.Signature);
            Assert.Equal("Valid", result3.Signature.Status);

            // Crucial assertion 3: Database was NOT re-initialized because _databaseLoaded was true
            Assert.Equal(1, dbLoadInvocationCount);
            Assert.Equal(2, scanInvocationCount);
        }
        finally
        {
            DieWrapper.ResetForTesting();
        }
    }

    [Fact]
    public async Task MachOBinary_ReturnsUnknownRiskAndManualReviewPipeline_NeverClean()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        // 32-byte minimal 64-bit Mach-O binary header (MH_MAGIC_64, CPU_TYPE_X86_64, MH_EXECUTE)
        byte[] machoHdr = new byte[]
        {
            0xcf, 0xfa, 0xed, 0xfe, // magic: MH_MAGIC_64 (0xfeedfacf little-endian)
            0x07, 0x00, 0x00, 0x01, // cputype: CPU_TYPE_X86_64 (0x01000007)
            0x03, 0x00, 0x00, 0x00, // cpusubtype: CPU_SUBTYPE_I386_ALL
            0x02, 0x00, 0x00, 0x00, // filetype: MH_EXECUTE
            0x00, 0x00, 0x00, 0x00, // ncmds: 0
            0x00, 0x00, 0x00, 0x00, // sizeofcmds: 0
            0x00, 0x00, 0x00, 0x00, // flags: 0
            0x00, 0x00, 0x00, 0x00  // reserved: 0
        };

        string tempMacho = Path.Combine(Path.GetTempPath(), $"test_sample_{Guid.NewGuid():N}.macho");
        await File.WriteAllBytesAsync(tempMacho, machoHdr);

        try
        {
            var result = await pipeline.RunTriageAsync(tempMacho);

            Assert.Equal("macho", result.FileType);
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains(result.RiskFactors, rf => rf.Contains("Mach-O"));
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempMacho))
            {
                File.Delete(tempMacho);
            }
        }
    }

    [Fact]
    public async Task SignatureVerifier_ReturnsErrorStatusCode_SetsPartialFailureAndUnknownRisk_WithoutThrowing()
    {
        var env = new WindowsEnvironment();
        // Verifier returns error status code 0x800B0101 (CERT_E_EXPIRED) without throwing
        var expiredVerifier = new FixedSignatureVerifier("0x800B0101");
        ITriagePipeline pipeline = new TriageService(expiredVerifier, env);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.True(result.AnalyzerErrors.ContainsKey("authenticode"));
        Assert.Contains("0x800B0101", result.AnalyzerErrors["authenticode"]);
        Assert.Equal("0x800B0101", result.Signature.Status);
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.NotEqual("CLEAN", result.RiskLevel);
        Assert.Equal("manual.review", result.RecommendedPipeline);
        Assert.Contains("0x800B0101", result.MarkdownReport);
        Assert.Contains("manual.review", result.MarkdownReport);
        Assert.Contains("⚠️ Phân tích chưa hoàn tất hoặc gặp lỗi analyzer", result.MarkdownReport);
        Assert.DoesNotContain("Tệp sạch, cấu trúc rõ ràng", result.MarkdownReport);
    }

    [Fact]
    public void DieWrapper_ScanValidPeAndGenericBinary_ReturnsExpectedOutput()
    {
        var env = new WindowsEnvironment();
        string root = GetEcosystemRoot();
        string dbPath = Path.Combine(root, @"Rules\die_db");
        string peFile = Path.Combine(root, @"Python_Runtime\python.exe");
        string peResult = DieWrapper.Scan(peFile, dbPath) ?? "";
        Assert.NotEmpty(peResult);
        Assert.Contains("PE", peResult);

        string tempFile = Path.Combine(Path.GetTempPath(), $"die_generic_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(tempFile, new byte[512]);
            string? randResult = DieWrapper.Scan(tempFile, dbPath);
            Assert.NotNull(randResult);
            Assert.Contains("Binary", randResult);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Binwalk_ObjectWithFileMap_ParsedCorrectlyInPipeline()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempFile = Path.Combine(Path.GetTempPath(), $"embedded_zip_{Guid.NewGuid():N}.bin");
        try
        {
            // Create a file containing a ZIP header so Binwalk v3 detects it
            byte[] zipHeader = new byte[128];
            zipHeader[0] = 0x50; zipHeader[1] = 0x4B; zipHeader[2] = 0x03; zipHeader[3] = 0x04;
            await File.WriteAllBytesAsync(tempFile, zipHeader);

            var result = await pipeline.RunTriageAsync(tempFile);

            Assert.False(result.AnalyzerErrors.ContainsKey("binwalk"), "Binwalk output with 'file_map' must not be marked as analyzer error.");
            Assert.True(result.DeepScanResults.Count > 0, "Binwalk should extract detected embedded entries.");
            Assert.Contains(result.DeepScanResults, e => e.Description.Contains("ZIP"));
            Assert.Contains("## 6. Tệp Nhúng Bên Trong", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task BinwalkCorruptedOutput_SetsAnalyzerErrorAndPartialFailure()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        var analyzer = new UnknownReturningSubprocessAnalyzer();
        ITriagePipeline pipeline = new TriageService(
            verifier,
            env,
            analyzer,
            binwalkScanner: _ => "{corrupted-not-json}");

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.True(result.AnalyzerErrors.ContainsKey("binwalk"), "Expected 'binwalk' analyzer error for corrupted JSON output");
        Assert.Contains("Corrupted Binwalk JSON", result.AnalyzerErrors["binwalk"]);
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.Contains(result.RiskFactors, rf => rf.Contains("Không thể giải mã JSON từ Binwalk"));
        Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
    }

    [Fact]
    public async Task BinwalkInvalidSchema_MissingFileMap_SetsAnalyzerErrorAndPartialFailure()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        var analyzer = new UnknownReturningSubprocessAnalyzer();
        ITriagePipeline pipeline = new TriageService(
            verifier,
            env,
            analyzer,
            binwalkScanner: _ => "{\"file_path\":\"test\"}");

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.True(result.AnalyzerErrors.ContainsKey("binwalk"));
        Assert.Equal("Binwalk JSON object missing 'file_map' array.", result.AnalyzerErrors["binwalk"]);
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.Contains(result.RiskFactors, rf => rf.Contains("thiếu trường 'file_map'"));
    }

    [Fact]
    public async Task BinwalkEmptyOutput_SetsAnalyzerErrorAndPartialFailure()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        var analyzer = new UnknownReturningSubprocessAnalyzer();
        ITriagePipeline pipeline = new TriageService(
            verifier,
            env,
            analyzer,
            binwalkScanner: _ => "   ");

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.True(result.AnalyzerErrors.ContainsKey("binwalk"));
        Assert.Equal("Binwalk scan returned empty output.", result.AnalyzerErrors["binwalk"]);
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.Contains(result.RiskFactors, rf => rf.Contains("trả về kết quả rỗng"));
    }

    [Fact]
    public async Task HashFailure_IsIsolated_SetsPartialFailureAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempLockedFile = Path.Combine(Path.GetTempPath(), $"locked_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(tempLockedFile, new byte[64]);

        try
        {
            // Lock file exclusively to force hash failure
            using var lockStream = new FileStream(tempLockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var result = await pipeline.RunTriageAsync(tempLockedFile);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("hash"), "Expected 'hash' analyzer error for locked file");
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempLockedFile)) File.Delete(tempLockedFile);
        }
    }

    [Fact]
    public async Task ProcessTree_DescendantProcessesKilledOnCancellation()
    {
        var env = new WindowsEnvironment();
        string tempScript = Path.Combine(Path.GetTempPath(), $"tree_spawner_{Guid.NewGuid():N}.py");
        string tempTarget = Path.Combine(Path.GetTempPath(), $"tree_target_{Guid.NewGuid():N}.bin");
        string pidFile = tempTarget + ".pid";

        try
        {
            await File.WriteAllBytesAsync(tempTarget, new byte[] { 1, 2, 3 });
            string scriptContent =
@"import subprocess, sys, time
target = sys.argv[1]
pid_file = target + '.pid'
p = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])
with open(pid_file, 'w') as f:
    f.write(str(p.pid))
sys.stdout.flush()
time.sleep(60)
";
            await File.WriteAllTextAsync(tempScript, scriptContent);

            var analyzer = new DirectTriageSubprocessAnalyzer(env)
            {
                ScriptPathOverride = tempScript
            };

            using var cts = new CancellationTokenSource();
            var analyzeTask = analyzer.AnalyzeAsync(tempTarget, cts.Token);

            int childPid = 0;
            for (int i = 0; i < 50; i++)
            {
                if (File.Exists(pidFile) && new FileInfo(pidFile).Length > 0)
                {
                    if (int.TryParse((await File.ReadAllTextAsync(pidFile)).Trim(), out childPid) && childPid > 0)
                    {
                        break;
                    }
                }
                await Task.Delay(100);
            }
            Assert.True(childPid > 0, "Descendant child process must have started.");

            var childProc = Process.GetProcessById(childPid);
            Assert.False(childProc.HasExited, "Child process should be active before cancellation.");

            // Cancel analyzer execution - this must trigger DirectTriageSubprocessAnalyzer's process.Kill(true)
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await analyzeTask);

            await Task.Delay(500);

            bool childIsDead = false;
            try
            {
                childIsDead = Process.GetProcessById(childPid).HasExited;
            }
            catch (ArgumentException)
            {
                childIsDead = true;
            }
            Assert.True(childIsDead, "Descendant process must be killed by DirectTriageSubprocessAnalyzer upon cancellation.");
        }
        finally
        {
            if (File.Exists(tempScript)) File.Delete(tempScript);
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
            if (File.Exists(pidFile)) File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task DirectTriageSubprocess_RealTimeout_TerminatesProcessTreeAndReturnsTimeoutResult()
    {
        var env = new WindowsEnvironment();
        string tempScript = Path.Combine(Path.GetTempPath(), $"timeout_spawner_{Guid.NewGuid():N}.py");
        string tempTarget = Path.Combine(Path.GetTempPath(), $"timeout_target_{Guid.NewGuid():N}.bin");
        string pidFile = tempTarget + ".pid";

        try
        {
            await File.WriteAllBytesAsync(tempTarget, new byte[] { 1, 2, 3 });
            string scriptContent =
@"import subprocess, sys, time
target = sys.argv[1]
pid_file = target + '.pid'
p = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])
with open(pid_file, 'w') as f:
    f.write(str(p.pid))
sys.stdout.flush()
time.sleep(60)
";
            await File.WriteAllTextAsync(tempScript, scriptContent);

            var analyzer = new DirectTriageSubprocessAnalyzer(env)
            {
                ScriptPathOverride = tempScript,
                Timeout = TimeSpan.FromSeconds(1) // 1 second timeout
            };

            var resultJson = await analyzer.AnalyzeAsync(tempTarget);

            // 1. Assert timeout result JSON properties
            Assert.True(resultJson.TryGetProperty("is_timeout", out var isTimeoutProp) && isTimeoutProp.GetBoolean());
            Assert.True(resultJson.TryGetProperty("error_code", out var errCodeProp) && errCodeProp.GetString() == "TIMEOUT");
            Assert.True(resultJson.TryGetProperty("recommended_pipeline", out var recProp) && recProp.GetString() == "error.timeout");
            Assert.True(resultJson.TryGetProperty("error", out var errProp) && errProp.GetString()!.Contains("timed out"));

            // 2. Assert descendant child process was terminated by process.Kill(true)
            int childPid = 0;
            for (int i = 0; i < 50; i++)
            {
                if (File.Exists(pidFile) && new FileInfo(pidFile).Length > 0)
                {
                    if (int.TryParse((await File.ReadAllTextAsync(pidFile)).Trim(), out childPid) && childPid > 0)
                    {
                        break;
                    }
                }
                await Task.Delay(50);
            }

            Assert.True(childPid > 0, "Descendant child process must have started and recorded its PID before timeout.");

            await Task.Delay(500);
            bool childIsDead = false;
            try
            {
                childIsDead = Process.GetProcessById(childPid).HasExited;
            }
            catch (ArgumentException)
            {
                childIsDead = true;
            }
            Assert.True(childIsDead, "Descendant child process must be killed upon subprocess timeout.");

            // 3. Verify integrated pipeline handling of this timeout analyzer
            var verifier = new AuthenticodeVerifier();
            ITriagePipeline pipeline = new TriageService(verifier, env, analyzer);
            var pipelineResult = await pipeline.RunTriageAsync(tempTarget);

            Assert.Equal(TriageStatus.TimedOut, pipelineResult.Status);
            Assert.True(pipelineResult.IsTimeout);
            Assert.Equal("UNKNOWN", pipelineResult.RiskLevel);
            Assert.Equal("manual.review", pipelineResult.RecommendedPipeline);
            Assert.NotNull(pipelineResult.SubprocessAnalyzerError);
            Assert.Contains("timed out", pipelineResult.SubprocessAnalyzerError);
        }
        finally
        {
            if (File.Exists(tempScript)) File.Delete(tempScript);
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
            if (File.Exists(pidFile)) File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task AxmlParser_ValidBinaryXmlChunk_ParsesCorrectly()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"valid_axml_{Guid.NewGuid():N}.apk");
        try
        {
            byte[] manifestBytes = BuildValidBinaryManifestBytes();
            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var entryStream = manifestEntry.Open())
                {
                    entryStream.Write(manifestBytes, 0, manifestBytes.Length);
                }

                var dexEntry = zip.CreateEntry("classes.dex");
                using (var entryStream = dexEntry.Open())
                {
                    byte[] dexStub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                    entryStream.Write(dexStub, 0, dexStub.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal("apk", result.FileType);
            Assert.Equal("android", result.Platform);
            Assert.False(result.AnalyzerErrors.ContainsKey("axml"), $"Unexpected axml error: {result.AnalyzerErrors.GetValueOrDefault("axml")}");
            Assert.NotNull(result.AndroidMetadata);
            Assert.Equal("com.example.validapp", result.AndroidMetadata.PackageName);
            Assert.Equal("100", result.AndroidMetadata.VersionCode);
            Assert.True(result.AndroidMetadata.PermissionsCount >= 1);
            Assert.Equal("com.example.validapp.MainActivity", result.AndroidMetadata.MainActivity);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    [Fact]
    public async Task Apk_CorruptOrMissingManifest_SetsAnalyzerErrorAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempMissingManifestApk = Path.Combine(Path.GetTempPath(), $"no_manifest_{Guid.NewGuid():N}.apk");
        string tempCorruptManifestApk = Path.Combine(Path.GetTempPath(), $"corrupt_manifest_{Guid.NewGuid():N}.apk");

        try
        {
            // Case 1: Missing manifest
            using (var zipStream = new FileStream(tempMissingManifestApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var dexEntry = zip.CreateEntry("classes.dex");
                using var entryStream = dexEntry.Open();
                byte[] dexStub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                entryStream.Write(dexStub, 0, dexStub.Length);
            }

            var resultMissing = await pipeline.RunTriageAsync(tempMissingManifestApk);
            Assert.Equal(TriageStatus.PartialFailure, resultMissing.Status);
            Assert.True(resultMissing.AnalyzerErrors.ContainsKey("axml"));
            Assert.Contains("AndroidManifest.xml not found", resultMissing.AnalyzerErrors["axml"]);
            Assert.Equal("UNKNOWN", resultMissing.RiskLevel);
            Assert.NotEqual("CLEAN", resultMissing.RiskLevel);
            Assert.Contains("CHƯA XÁC ĐỊNH", resultMissing.MarkdownReport);

            // Case 2: Corrupt manifest
            using (var zipStream = new FileStream(tempCorruptManifestApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var entryStream = manifestEntry.Open())
                {
                    byte[] junk = System.Text.Encoding.UTF8.GetBytes("Corrupted junk that is not binary AXML nor valid XML");
                    entryStream.Write(junk, 0, junk.Length);
                }

                var dexEntry = zip.CreateEntry("classes.dex");
                using var dexEntryStream = dexEntry.Open();
                byte[] dexStub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                dexEntryStream.Write(dexStub, 0, dexStub.Length);
            }

            var resultCorrupt = await pipeline.RunTriageAsync(tempCorruptManifestApk);
            Assert.Equal(TriageStatus.PartialFailure, resultCorrupt.Status);
            Assert.True(resultCorrupt.AnalyzerErrors.ContainsKey("axml"));
            Assert.Equal("UNKNOWN", resultCorrupt.RiskLevel);
            Assert.NotEqual("CLEAN", resultCorrupt.RiskLevel);
            Assert.Contains("CHƯA XÁC ĐỊNH", resultCorrupt.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempMissingManifestApk)) File.Delete(tempMissingManifestApk);
            if (File.Exists(tempCorruptManifestApk)) File.Delete(tempCorruptManifestApk);
        }
    }

    [Fact]
    public async Task Authenticode_InvalidOrTamperedSignature_PreservedAndSetsUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var badDigestVerifier = new FixedSignatureVerifier("0x80096010");
        ITriagePipeline pipeline = new TriageService(badDigestVerifier, env);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.True(result.AnalyzerErrors.ContainsKey("authenticode"));
        Assert.Contains("0x80096010", result.AnalyzerErrors["authenticode"]);
        Assert.Contains(result.RiskFactors, rf => rf.Contains("0x80096010"));
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.DoesNotContain("Tệp sạch, cấu trúc rõ ràng", result.MarkdownReport);
        Assert.Contains("⚠️ Phân tích chưa hoàn tất hoặc gặp lỗi analyzer", result.MarkdownReport);

        // Native AuthenticodeVerifier on tampered PE binary
        string tempPe = Path.Combine(Path.GetTempPath(), $"tampered_{Guid.NewGuid():N}.exe");
        try
        {
            File.Copy(targetFile, tempPe, true);
            using (var fs = new FileStream(tempPe, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Seek(0x1000, SeekOrigin.Begin);
                fs.Write(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, 0, 4);
            }

            var nativeVerifier = new AuthenticodeVerifier();
            var sigResult = nativeVerifier.Verify(tempPe);

            Assert.NotEqual("NotSigned", sigResult.Status);
            Assert.NotEqual("Valid", sigResult.Status);
            Assert.StartsWith("0x", sigResult.Status);
        }
        finally
        {
            if (File.Exists(tempPe)) File.Delete(tempPe);
        }
    }

    [Fact]
    public async Task HashCalculationFailure_BeforeRiskAssessment_SetsUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempLockedPe = Path.Combine(Path.GetTempPath(), $"locked_pe_{Guid.NewGuid():N}.exe");
        string root = GetEcosystemRoot();
        string pythonExe = Path.Combine(root, @"Python_Runtime\python.exe");
        File.Copy(pythonExe, tempLockedPe, true);

        try
        {
            using var lockStream = new FileStream(tempLockedPe, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var result = await pipeline.RunTriageAsync(tempLockedPe);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("hash"), "Hash error must be registered when file is locked.");
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.NotEqual("LOW", result.RiskLevel);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempLockedPe)) File.Delete(tempLockedPe);
        }
    }

    private static byte[] BuildValidBinaryManifestBytes()
    {
        string[] strings = new[]
        {
            "manifest",
            "package",
            "com.example.validapp",
            "versionCode",
            "100",
            "uses-permission",
            "name",
            "android.permission.INTERNET",
            "application",
            "activity",
            "com.example.validapp.MainActivity"
        };

        var offsets = new List<int>();
        var strData = new MemoryStream();
        foreach (var s in strings)
        {
            offsets.Add((int)strData.Length);
            byte[] encoded = System.Text.Encoding.Unicode.GetBytes(s);
            using var bwStr = new BinaryWriter(strData, System.Text.Encoding.Unicode, leaveOpen: true);
            bwStr.Write((ushort)s.Length);
            bwStr.Write(encoded);
            bwStr.Write((ushort)0);
        }
        while (strData.Length % 4 != 0)
        {
            strData.WriteByte(0);
        }

        uint headerSize = 28;
        uint stringCount = (uint)strings.Length;
        uint stringsStart = headerSize + 4 * stringCount;
        uint spChunkSize = (uint)(stringsStart + strData.Length);

        using var spMs = new MemoryStream();
        using (var bw = new BinaryWriter(spMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((ushort)0x0001);
            bw.Write((ushort)headerSize);
            bw.Write(spChunkSize);
            bw.Write(stringCount);
            bw.Write((uint)0);
            bw.Write((uint)0);
            bw.Write(stringsStart);
            bw.Write((uint)0);
            foreach (var off in offsets)
            {
                bw.Write((uint)off);
            }
            bw.Write(strData.ToArray());
        }
        byte[] spChunk = spMs.ToArray();

        using var bodyMs = new MemoryStream();
        using (var bw = new BinaryWriter(bodyMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            // Element 1: manifest
            bw.Write((ushort)0x0102);
            bw.Write((ushort)16);
            bw.Write((uint)(36 + 40));
            bw.Write((uint)1);
            bw.Write(0xFFFFFFFF);
            bw.Write(0xFFFFFFFF);
            bw.Write((uint)0);
            bw.Write((ushort)20);
            bw.Write((ushort)20);
            bw.Write((ushort)2);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            // attr1: package
            bw.Write(0xFFFFFFFF); bw.Write((uint)1); bw.Write((uint)2); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)2);
            // attr2: versionCode
            bw.Write(0xFFFFFFFF); bw.Write((uint)3); bw.Write((uint)4); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)4);

            // Element 2: uses-permission
            bw.Write((ushort)0x0102);
            bw.Write((ushort)16);
            bw.Write((uint)(36 + 20));
            bw.Write((uint)2);
            bw.Write(0xFFFFFFFF);
            bw.Write(0xFFFFFFFF);
            bw.Write((uint)5);
            bw.Write((ushort)20);
            bw.Write((ushort)20);
            bw.Write((ushort)1);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            // attr_perm: name
            bw.Write(0xFFFFFFFF); bw.Write((uint)6); bw.Write((uint)7); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)7);
            // End Element 2
            bw.Write((ushort)0x0103); bw.Write((ushort)16); bw.Write((uint)24); bw.Write((uint)2); bw.Write(0xFFFFFFFF); bw.Write(0xFFFFFFFF); bw.Write((uint)5);

            // Element 3: application
            bw.Write((ushort)0x0102);
            bw.Write((ushort)16);
            bw.Write((uint)36);
            bw.Write((uint)3);
            bw.Write(0xFFFFFFFF);
            bw.Write(0xFFFFFFFF);
            bw.Write((uint)8);
            bw.Write((ushort)20);
            bw.Write((ushort)20);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)0);

            // Element 4: activity
            bw.Write((ushort)0x0102);
            bw.Write((ushort)16);
            bw.Write((uint)(36 + 20));
            bw.Write((uint)4);
            bw.Write(0xFFFFFFFF);
            bw.Write(0xFFFFFFFF);
            bw.Write((uint)9);
            bw.Write((ushort)20);
            bw.Write((ushort)20);
            bw.Write((ushort)1);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            // attr_act: name
            bw.Write(0xFFFFFFFF); bw.Write((uint)6); bw.Write((uint)10); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)10);
            // End Element 4
            bw.Write((ushort)0x0103); bw.Write((ushort)16); bw.Write((uint)24); bw.Write((uint)4); bw.Write(0xFFFFFFFF); bw.Write(0xFFFFFFFF); bw.Write((uint)9);

            // End Element 3
            bw.Write((ushort)0x0103); bw.Write((ushort)16); bw.Write((uint)24); bw.Write((uint)3); bw.Write(0xFFFFFFFF); bw.Write(0xFFFFFFFF); bw.Write((uint)8);

            // End Element 1
            bw.Write((ushort)0x0103); bw.Write((ushort)16); bw.Write((uint)24); bw.Write((uint)1); bw.Write(0xFFFFFFFF); bw.Write(0xFFFFFFFF); bw.Write((uint)0);
        }
        byte[] bodyBytes = bodyMs.ToArray();

        using var totalMs = new MemoryStream();
        using (var bw = new BinaryWriter(totalMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            uint totalSize = (uint)(8 + spChunk.Length + bodyBytes.Length);
            bw.Write((uint)0x00080003);
            bw.Write(totalSize);
            bw.Write(spChunk);
            bw.Write(bodyBytes);
        }
        return totalMs.ToArray();
    }

    private class FixedSignatureVerifier : ISignatureVerifier
    {
        private readonly string _status;
        public FixedSignatureVerifier(string status) => _status = status;
        public SignatureResult Verify(string filePath) => new SignatureResult { Status = _status, Subject = null };
    }

    private class BusySignatureVerifier : ISignatureVerifier
    {
        public SignatureResult Verify(string filePath) =>
            throw new InvalidOperationException("Authenticode verifier is busy executing a prior operation.");
    }

    private class CustomDieDbEnvironment : IPlatformEnvironment
    {
        private readonly string _dieDbPath;
        public CustomDieDbEnvironment(string dieDbPath) => _dieDbPath = dieDbPath;
        public string PythonExecutable => Path.Combine(GetEcosystemRoot(), @"Python_Runtime\python.exe");
        public string DieDbPath => _dieDbPath;
        public void SetLibrarySearchPath(string path) { }
    }

    private class TruncatedReturningSubprocessAnalyzer : IDirectTriageSubprocessAnalyzer
    {
        public Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var dict = new System.Collections.Generic.Dictionary<string, object?>
            {
                ["exists"] = true,
                ["file_type"] = "pe",
                ["platform"] = "windows",
                ["size"] = 1000000,
                ["size_truncated"] = true,
                ["markdown_report"] = "## 1. Thông Tin Tệp Tin & Mã Băm\n| Kích thước | 1,000,000 bytes |\n",
                ["recommended_pipeline"] = "windows.clean_pe"
            };
            var json = JsonSerializer.Serialize(dict);
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(doc.RootElement.Clone());
        }
    }

    private class MidExecutionCancellingAnalyzer : IDirectTriageSubprocessAnalyzer
    {
        public CancellationTokenSource? TokenSourceToCancel { get; set; }

        public async Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            await Task.Delay(15, cancellationToken);
            TokenSourceToCancel?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }
    }

    private class ErrorReturningSubprocessAnalyzer : IDirectTriageSubprocessAnalyzer
    {
        private readonly string _error;
        public ErrorReturningSubprocessAnalyzer(string error) => _error = error;

        public Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var dict = new System.Collections.Generic.Dictionary<string, object?>
            {
                ["exists"] = false,
                ["error"] = _error,
                ["error_code"] = "ERROR",
                ["is_timeout"] = false,
                ["recommended_pipeline"] = "error.triage_failed"
            };
            var json = JsonSerializer.Serialize(dict);
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(doc.RootElement.Clone());
        }
    }

    private class TimeoutReturningSubprocessAnalyzer : IDirectTriageSubprocessAnalyzer
    {
        public Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var dict = new System.Collections.Generic.Dictionary<string, object?>
            {
                ["exists"] = false,
                ["error"] = "Subprocess triage timed out after 15 seconds.",
                ["error_code"] = "TIMEOUT",
                ["is_timeout"] = true,
                ["recommended_pipeline"] = "error.timeout"
            };
            var json = JsonSerializer.Serialize(dict);
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(doc.RootElement.Clone());
        }
    }

    private class UnknownReturningSubprocessAnalyzer : IDirectTriageSubprocessAnalyzer
    {
        public Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var dict = new System.Collections.Generic.Dictionary<string, object?>
            {
                ["exists"] = true,
                ["file_type"] = "unknown",
                ["platform"] = "unknown",
                ["packer"] = "unknown",
                ["size"] = 1024,
                ["size_truncated"] = false,
                ["entropy"] = new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["overall"] = 4.5,
                    ["sections"] = Array.Empty<object>()
                },
                ["markdown_report"] = "# 🛡️ Báo Cáo Phân Tích Tĩnh — Executive Triage\n**Tệp tin:** `" + filePath + "`  \n**Đánh giá Rủi ro:** ❓ **CHƯA XÁC ĐỊNH (0/100)**\n\n## 1. Thông Tin Tệp Tin & Mã Băm\n| Thuộc tính | Giá trị |\n|---|---|\n| **MD5** | `abc` |\n| **SHA256** | `def` |\n",
                ["recommended_pipeline"] = "manual.review"
            };
            var json = JsonSerializer.Serialize(dict);
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(doc.RootElement.Clone());
        }
    }

    private class DictionaryReturningSubprocessAnalyzer : IDirectTriageSubprocessAnalyzer
    {
        private readonly Dictionary<string, object?> _dict;
        public DictionaryReturningSubprocessAnalyzer(Dictionary<string, object?> dict) => _dict = dict;

        public Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(_dict);
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(doc.RootElement.Clone());
        }
    }

    [Fact]
    public async Task AxmlParser_TruncatedChunkAfterRoot_SetsAnalyzerErrorAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"truncated_axml_{Guid.NewGuid():N}.apk");
        try
        {
            byte[] validManifest = BuildValidBinaryManifestBytes();
            using var truncatedMs = new MemoryStream();
            using (var bw = new BinaryWriter(truncatedMs, System.Text.Encoding.Unicode, leaveOpen: true))
            {
                bw.Write(validManifest);
                // Append truncated start-element chunk (type=0x0102, header_size=16, chunk_size=80)
                bw.Write((ushort)0x0102);
                bw.Write((ushort)16);
                bw.Write((uint)80);
            }
            byte[] truncatedBytes = truncatedMs.ToArray();
            // Update AXML file size header
            using (var bw = new BinaryWriter(new MemoryStream(truncatedBytes)))
            {
                bw.Seek(4, SeekOrigin.Begin);
                bw.Write((uint)truncatedBytes.Length);
            }

            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var entryStream = manifestEntry.Open())
                {
                    entryStream.Write(truncatedBytes, 0, truncatedBytes.Length);
                }

                var dexEntry = zip.CreateEntry("classes.dex");
                using (var entryStream = dexEntry.Open())
                {
                    byte[] dexStub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                    entryStream.Write(dexStub, 0, dexStub.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for truncated chunk");
            Assert.Contains("truncated", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);

            // Valid parsed fields from root element must be preserved
            Assert.NotNull(result.AndroidMetadata);
            Assert.Equal("com.example.validapp", result.AndroidMetadata.PackageName);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    [Fact]
    public async Task AxmlParser_OverflowingAttributesChunkAfterRoot_SetsAnalyzerErrorAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"overflow_axml_{Guid.NewGuid():N}.apk");
        try
        {
            byte[] validBytes = BuildValidBinaryManifestBytes();
            using var overflowMs = new MemoryStream();
            overflowMs.Write(validBytes, 0, validBytes.Length);
            using (var bw = new BinaryWriter(overflowMs, System.Text.Encoding.Unicode, leaveOpen: true))
            {
                // Child chunk claiming chunk_size=36 but declaring attr_count=5 (overflowing chunk boundaries)
                bw.Write((ushort)0x0102);
                bw.Write((ushort)16);
                bw.Write((uint)36);
                bw.Write((uint)2);
                bw.Write(0xFFFFFFFF);
                bw.Write(0xFFFFFFFF);
                bw.Write((uint)3);
                bw.Write((ushort)20);
                bw.Write((ushort)20);
                bw.Write((ushort)5);
                bw.Write((ushort)0);
                bw.Write((ushort)0);
                bw.Write((ushort)0);
            }
            byte[] overflowBytes = overflowMs.ToArray();
            using (var bw = new BinaryWriter(new MemoryStream(overflowBytes)))
            {
                bw.Seek(4, SeekOrigin.Begin);
                bw.Write((uint)overflowBytes.Length);
            }

            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var entryStream = manifestEntry.Open())
                {
                    entryStream.Write(overflowBytes, 0, overflowBytes.Length);
                }

                var dexEntry = zip.CreateEntry("classes.dex");
                using (var entryStream = dexEntry.Open())
                {
                    byte[] dexStub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                    entryStream.Write(dexStub, 0, dexStub.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for overflowing attribute chunk");
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);

            // Valid parsed fields from root element must be preserved
            Assert.NotNull(result.AndroidMetadata);
            Assert.Equal("com.example.validapp", result.AndroidMetadata.PackageName);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    [Fact]
    public async Task YaraFailure_SetsAnalyzerErrorAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();

        var yaraErrorAnalyzer = new DictionaryReturningSubprocessAnalyzer(new Dictionary<string, object?>
        {
            ["exists"] = true,
            ["file_type"] = "pe",
            ["platform"] = "windows",
            ["magic"] = "4d 5a",
            ["hashes"] = new Dictionary<string, string> { ["md5"] = "aaa", ["sha256"] = "bbb" },
            ["analyzer_errors"] = new Dictionary<string, string> { ["yara"] = "YARA scanning failed: Simulated memory access fault" },
            ["risk_assessment"] = new Dictionary<string, object?>
            {
                ["risk_score"] = 0,
                ["risk_level"] = "UNKNOWN",
                ["risk_factors"] = new List<string> { "Định dạng tệp không xác định hoặc phân tích cấu trúc gặp lỗi / chưa hoàn tất." }
            }
        });

        ITriagePipeline pipeline = new TriageService(verifier, env, yaraErrorAnalyzer);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.True(result.AnalyzerErrors.ContainsKey("yara"));
        Assert.Contains("Simulated memory access fault", result.AnalyzerErrors["yara"]);
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.NotEqual("CLEAN", result.RiskLevel);
        Assert.Contains("Cảnh báo lỗi phân tích (yara)", result.MarkdownReport);
        Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
    }

    [Fact]
    public async Task DieNativeAnalyzer_WhenBusy_SetsPartialFailureAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();

        var busyDieScanner = new Func<string, string, string?>((filePath, dbPath) =>
        {
            throw new InvalidOperationException("DIE native analyzer is busy executing a prior operation.");
        });

        ITriagePipeline pipeline = new TriageService(
            verifier,
            env,
            dieScanner: busyDieScanner);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.True(result.AnalyzerErrors.ContainsKey("die"));
        Assert.Contains("busy", result.AnalyzerErrors["die"]);
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.NotEqual("CLEAN", result.RiskLevel);
    }

    [Fact]
    public async Task TriageOrchestrator_ThrowsIfNotConfigured()
    {
#pragma warning disable CS0618
        try
        {
            TriageOrchestrator.Configure((ITriagePipeline)null!);
            TriageOrchestrator.PipelineResolver = null;

            string root = GetEcosystemRoot();
            string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await TriageOrchestrator.RunTriageAsync(targetFile);
            });
            Assert.Contains("TriageOrchestrator must be configured", ex.Message);
        }
        finally
        {
            // Restore configuration for other tests
            TriageOrchestrator.Configure(new TriageService(new AuthenticodeVerifier(), new WindowsEnvironment()));
        }
#pragma warning restore CS0618
    }

    [Fact]
    public async Task Authenticode_CatalogErrorOnNonPe_PreservedAsHexAndSetsUnknownRisk()
    {
        var env = new WindowsEnvironment();
        // Use real AuthenticodeVerifier injecting catalog failure result 0x80004005
        var catalogErrorVerifier = new AuthenticodeVerifier(path => 0x80004005);
        ITriagePipeline pipeline = new TriageService(catalogErrorVerifier, env);

        // 1. Direct unit verification on non-PE file (embeddedResult is TRUST_E_SUBJECT_FORM_UNKNOWN, enters catalog branch)
        string tempTextFile = Path.Combine(Path.GetTempPath(), $"nonpe_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempTextFile, "Non-PE plain text payload for catalog lookup error testing.");
            var directResult = catalogErrorVerifier.Verify(tempTextFile);
            Assert.Equal("0x80004005", directResult.Status);
        }
        finally
        {
            if (File.Exists(tempTextFile)) File.Delete(tempTextFile);
        }

        // 2. Pipeline integration test with unsigned PE (embeddedResult is TRUST_E_NOSIGNATURE, enters catalog branch)
        string root = GetEcosystemRoot();
        string pythonExe = Path.Combine(root, @"Python_Runtime\python.exe");
        Assert.True(File.Exists(pythonExe), $"Python executable not found at {pythonExe}");

        string tempPe = Path.Combine(Path.GetTempPath(), $"unsigned_pe_{Guid.NewGuid():N}.exe");
        try
        {
            byte[] peBytes = await File.ReadAllBytesAsync(pythonExe);
            // Zero out Security Directory (Certificate Table) in PE32+ header to make it an unsigned PE
            int peOffset = BitConverter.ToInt32(peBytes, 0x3C);
            int certDirOffset = peOffset + 24 + 112 + 4 * 8;
            Array.Clear(peBytes, certDirOffset, 8);
            await File.WriteAllBytesAsync(tempPe, peBytes);

            var directPeResult = catalogErrorVerifier.Verify(tempPe);
            Assert.Equal("0x80004005", directPeResult.Status);

            var result = await pipeline.RunTriageAsync(tempPe);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("authenticode"));
            Assert.Equal("0x80004005", result.Signature.Status);
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Contains("0x80004005", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempPe)) File.Delete(tempPe);
        }
    }

    [Fact]
    public async Task Authenticode_TamperedPeFile_DirectlyReturnsBadDigestAndSetsUnknownRisk()
    {
        string root = GetEcosystemRoot();
        string pythonExe = Path.Combine(root, @"Python_Runtime\python.exe");
        Assert.True(File.Exists(pythonExe), $"Python executable not found at {pythonExe}");

        string tempPe = Path.Combine(Path.GetTempPath(), $"tampered_{Guid.NewGuid():N}.exe");
        try
        {
            byte[] peBytes = await File.ReadAllBytesAsync(pythonExe);
            // Invert bytes in the code section (offset 0x1000)
            peBytes[0x1000] ^= 0xFF;
            await File.WriteAllBytesAsync(tempPe, peBytes);

            var verifier = new AuthenticodeVerifier();
            var signatureResult = verifier.Verify(tempPe);

            // 0x80096010 is TRUST_E_BAD_DIGEST (hash mismatch / tampered)
            Assert.Equal("0x80096010", signatureResult.Status);

            var env = new WindowsEnvironment();
            ITriagePipeline pipeline = new TriageService(verifier, env);
            var result = await pipeline.RunTriageAsync(tempPe);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("authenticode"));
            Assert.Contains("0x80096010", result.AnalyzerErrors["authenticode"]);
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
        }
        finally
        {
            if (File.Exists(tempPe))
            {
                File.Delete(tempPe);
            }
        }
    }

    [Fact]
    public async Task Authenticode_WhenBusy_SetsPartialFailureAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var busyVerifier = new BusySignatureVerifier();
        ITriagePipeline pipeline = new TriageService(busyVerifier, env);

        string root = GetEcosystemRoot();
        string targetFile = Path.Combine(root, @"Python_Runtime\python.exe");

        var result = await pipeline.RunTriageAsync(targetFile);

        Assert.Equal(TriageStatus.PartialFailure, result.Status);
        Assert.True(result.AnalyzerErrors.ContainsKey("authenticode"));
        Assert.Contains("busy", result.AnalyzerErrors["authenticode"]);
        Assert.Equal("UNKNOWN", result.RiskLevel);
        Assert.NotEqual("CLEAN", result.RiskLevel);
        Assert.Equal("Busy", result.Signature.Status);
    }

    [Fact]
    public async Task AxmlParser_EndElement_InvalidNameIndex_SetsAnalyzerErrorAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"invalid_endelem_axml_{Guid.NewGuid():N}.apk");
        try
        {
            byte[] validBytes = BuildValidBinaryManifestBytes();
            using var invalidMs = new MemoryStream();
            invalidMs.Write(validBytes, 0, validBytes.Length);
            using (var bw = new BinaryWriter(invalidMs, System.Text.Encoding.Unicode, leaveOpen: true))
            {
                // End element with invalid name_idx = 0xFFFFFFFF
                bw.Write((ushort)0x0103);
                bw.Write((ushort)16);
                bw.Write((uint)24);
                bw.Write((uint)1);
                bw.Write(0xFFFFFFFF);
                bw.Write(0xFFFFFFFF);
                bw.Write(0xFFFFFFFF);
            }
            byte[] invalidBytes = invalidMs.ToArray();
            using (var bw = new BinaryWriter(new MemoryStream(invalidBytes)))
            {
                bw.Seek(4, SeekOrigin.Begin);
                bw.Write((uint)invalidBytes.Length);
            }

            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var entryStream = manifestEntry.Open())
                {
                    entryStream.Write(invalidBytes, 0, invalidBytes.Length);
                }

                var dexEntry = zip.CreateEntry("classes.dex");
                using (var entryStream = dexEntry.Open())
                {
                    byte[] dexStub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                    entryStream.Write(dexStub, 0, dexStub.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for invalid end-element name index");
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);

            // Valid parsed fields from root element must be preserved
            Assert.NotNull(result.AndroidMetadata);
            Assert.Equal("com.example.validapp", result.AndroidMetadata.PackageName);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    [Fact]
    public void AuthenticodeVerifier_RealCatalogVerification_DirectVerification()
    {
        var verifier = new AuthenticodeVerifier();
        string tempTextFile = Path.Combine(Path.GetTempPath(), $"unsigned_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempTextFile, "Sample content for testing Authenticode catalog lookup.");
            var result = verifier.Verify(tempTextFile);

            // A plain text file is not a PE binary, so embedded verification returns TRUST_E_SUBJECT_FORM_UNKNOWN,
            // and catalog verification finds no catalog match, returning NotApplicable.
            Assert.NotNull(result);
            Assert.Equal("NotApplicable", result.Status);
        }
        finally
        {
            if (File.Exists(tempTextFile)) File.Delete(tempTextFile);
        }
    }

    [Fact]
    public void AuthenticodeVerifier_VerifyCatalog_InvalidFile_DirectlyReturnsHexError()
    {
        // Direct test of AuthenticodeVerifier.VerifyCatalog on an invalid / inaccessible path
        string invalidPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.bin");
        uint result = AuthenticodeVerifier.VerifyCatalog(invalidPath);

        // When the file cannot be opened, the catch block in VerifyCatalog returns 0x80004005 (E_FAIL)
        Assert.Equal((uint)0x80004005, result);
    }

    [Fact]
    public async Task AxmlParser_HeaderFileSizeMismatch_SetsAnalyzerErrorAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"mismatch_hdr_axml_{Guid.NewGuid():N}.apk");
        try
        {
            byte[] validBytes = BuildValidBinaryManifestBytes();
            // Forge the header: total length is validBytes.Length (208+ bytes), but declare file_size = 8
            using (var bw = new BinaryWriter(new MemoryStream(validBytes)))
            {
                bw.Seek(4, SeekOrigin.Begin);
                bw.Write((uint)8); // Inconsistent header file_size
            }

            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var entryStream = manifestEntry.Open())
                {
                    entryStream.Write(validBytes, 0, validBytes.Length);
                }

                var dexEntry = zip.CreateEntry("classes.dex");
                using (var entryStream = dexEntry.Open())
                {
                    byte[] dexStub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                    entryStream.Write(dexStub, 0, dexStub.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for header file_size mismatch");
            Assert.Contains("inconsistent axml header file size", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    [Fact]
    public async Task AxmlParser_MultipleRootElements_SetsAnalyzerErrorAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"multiroot_axml_{Guid.NewGuid():N}.apk");
        try
        {
            byte[] validBytes = BuildValidBinaryManifestBytes();
            using var multiRootMs = new MemoryStream();
            multiRootMs.Write(validBytes, 0, validBytes.Length);
            using (var bw = new BinaryWriter(multiRootMs, System.Text.Encoding.Unicode, leaveOpen: true))
            {
                // Append a second root element (<manifest package="com.example.secondroot">)
                // Root 2 start-element
                bw.Write((ushort)0x0102);
                bw.Write((ushort)16);
                bw.Write((uint)(36 + 20));
                bw.Write((uint)5);
                bw.Write(0xFFFFFFFF);
                bw.Write(0xFFFFFFFF);
                bw.Write((uint)0); // name: manifest
                bw.Write((ushort)20);
                bw.Write((ushort)20);
                bw.Write((ushort)1);
                bw.Write((ushort)0);
                bw.Write((ushort)0);
                bw.Write((ushort)0);
                // attr: package -> com.example.validapp
                bw.Write(0xFFFFFFFF); bw.Write((uint)1); bw.Write((uint)2); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)2);

                // Root 2 end-element
                bw.Write((ushort)0x0103);
                bw.Write((ushort)16);
                bw.Write((uint)24);
                bw.Write((uint)5);
                bw.Write(0xFFFFFFFF);
                bw.Write(0xFFFFFFFF);
                bw.Write((uint)0);
            }
            byte[] multiRootBytes = multiRootMs.ToArray();
            using (var bw = new BinaryWriter(new MemoryStream(multiRootBytes)))
            {
                bw.Seek(4, SeekOrigin.Begin);
                bw.Write((uint)multiRootBytes.Length);
            }

            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var entryStream = manifestEntry.Open())
                {
                    entryStream.Write(multiRootBytes, 0, multiRootBytes.Length);
                }

                var dexEntry = zip.CreateEntry("classes.dex");
                using (var entryStream = dexEntry.Open())
                {
                    byte[] dexStub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                    entryStream.Write(dexStub, 0, dexStub.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for multiple root elements");
            Assert.Contains("multiple root elements", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    [Fact]
    public async Task RealLargeFile_Over200MB_WithValidPeHeader_SetsSizeTruncatedAndForcesUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(signatureVerifier, env);

        string root = GetEcosystemRoot();
        string pythonExe = Path.Combine(root, @"Python_Runtime\python.exe");
        Assert.True(File.Exists(pythonExe), $"Python executable not found at {pythonExe}");

        string tempLargePe = Path.Combine(Path.GetTempPath(), $"large_pe_{Guid.NewGuid():N}.exe");
        try
        {
            // Seed file with valid PE headers (first 4096 bytes of python.exe) then extend to 205 MiB
            const long TargetSize = 205L * 1024 * 1024;
            byte[] headerBuffer = new byte[4096];
            using (var src = File.OpenRead(pythonExe))
            {
                src.ReadExactly(headerBuffer, 0, headerBuffer.Length);
            }

            using (var fs = new FileStream(tempLargePe, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(headerBuffer, 0, headerBuffer.Length);
                fs.SetLength(TargetSize);
            }

            var result = await pipeline.RunTriageAsync(tempLargePe);

            // Invariants for large files:
            Assert.True(result.SizeTruncated, "File > 200 MiB must have SizeTruncated = true");
            Assert.Equal(TargetSize, result.Size);
            Assert.Equal("pe", result.FileType);
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains(result.RiskFactors, f => f.Contains("200 MiB"));
            Assert.Contains("Lưu ý tệp dung lượng lớn (> 200 MiB)", result.MarkdownReport);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempLargePe)) File.Delete(tempLargePe);
        }
    }

    private static byte[] BuildManifestWithInvalidAttributeNameIndex()
    {
        string[] strings = new[]
        {
            "manifest", "package", "com.example.validapp", "versionCode", "1",
            "uses-permission", "name", "android.permission.INTERNET"
        };
        var offsets = new List<int>();
        using var strData = new MemoryStream();
        foreach (var s in strings)
        {
            offsets.Add((int)strData.Length);
            byte[] encoded = System.Text.Encoding.Unicode.GetBytes(s);
            using var bwStr = new BinaryWriter(strData, System.Text.Encoding.Unicode, leaveOpen: true);
            bwStr.Write((ushort)s.Length);
            bwStr.Write(encoded);
            bwStr.Write((ushort)0);
        }
        while (strData.Length % 4 != 0)
        {
            strData.WriteByte(0);
        }

        uint headerSize = 28;
        uint stringCount = (uint)strings.Length;
        uint stringsStart = headerSize + 4 * stringCount;
        uint spChunkSize = (uint)(stringsStart + strData.Length);

        using var spMs = new MemoryStream();
        using (var bw = new BinaryWriter(spMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((ushort)0x0001);
            bw.Write((ushort)headerSize);
            bw.Write(spChunkSize);
            bw.Write(stringCount);
            bw.Write((uint)0);
            bw.Write((uint)0);
            bw.Write(stringsStart);
            bw.Write((uint)0);
            foreach (var off in offsets)
            {
                bw.Write((uint)off);
            }
            bw.Write(strData.ToArray());
        }
        byte[] spChunk = spMs.ToArray();

        using var bodyMs = new MemoryStream();
        using (var bw = new BinaryWriter(bodyMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            // Element 1: manifest
            bw.Write((ushort)0x0102);
            bw.Write((ushort)16);
            bw.Write((uint)(36 + 40));
            bw.Write((uint)1);
            bw.Write(0xFFFFFFFF);
            bw.Write(0xFFFFFFFF);
            bw.Write((uint)0);
            bw.Write((ushort)20);
            bw.Write((ushort)20);
            bw.Write((ushort)2);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            // attr1: package
            bw.Write(0xFFFFFFFF); bw.Write((uint)1); bw.Write((uint)2); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)2);
            // attr2: versionCode
            bw.Write(0xFFFFFFFF); bw.Write((uint)3); bw.Write((uint)4); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)4);

            // Element 2: uses-permission with INVALID a_name_idx = 0xFFFFFFFF
            bw.Write((ushort)0x0102);
            bw.Write((ushort)16);
            bw.Write((uint)(36 + 20));
            bw.Write((uint)2);
            bw.Write(0xFFFFFFFF);
            bw.Write(0xFFFFFFFF);
            bw.Write((uint)5);
            bw.Write((ushort)20);
            bw.Write((ushort)20);
            bw.Write((ushort)1);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            // attr_perm: ns=0xFFFFFFFF, a_name_idx=0xFFFFFFFF (INVALID!), a_val_str_idx=7
            bw.Write(0xFFFFFFFF); bw.Write(0xFFFFFFFF); bw.Write((uint)7); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)7);
            // End Element 2
            bw.Write((ushort)0x0103); bw.Write((ushort)16); bw.Write((uint)24); bw.Write((uint)2); bw.Write(0xFFFFFFFF); bw.Write(0xFFFFFFFF); bw.Write((uint)5);

            // End Element 1
            bw.Write((ushort)0x0103); bw.Write((ushort)16); bw.Write((uint)24); bw.Write((uint)1); bw.Write(0xFFFFFFFF); bw.Write(0xFFFFFFFF); bw.Write((uint)0);
        }
        byte[] bodyBytes = bodyMs.ToArray();

        using var totalMs = new MemoryStream();
        using (var bw = new BinaryWriter(totalMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            uint totalSize = (uint)(8 + spChunk.Length + bodyBytes.Length);
            bw.Write((uint)0x00080003);
            bw.Write(totalSize);
            bw.Write(spChunk);
            bw.Write(bodyBytes);
        }
        return totalMs.ToArray();
    }

    [Fact]
    public async Task AxmlParser_InvalidAttributeNameIndex_SetsAnalyzerErrorAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"invalid_attr_axml_{Guid.NewGuid():N}.apk");
        try
        {
            byte[] invalidBytes = BuildManifestWithInvalidAttributeNameIndex();

            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var entryStream = manifestEntry.Open())
                {
                    entryStream.Write(invalidBytes, 0, invalidBytes.Length);
                }

                var dexEntry = zip.CreateEntry("classes.dex");
                using (var entryStream = dexEntry.Open())
                {
                    byte[] dexStub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                    entryStream.Write(dexStub, 0, dexStub.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for invalid attribute name index");
            Assert.Contains("invalid attribute name index", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    [Fact]
    public async Task Apk_CorruptOrMissingManifest_HasManualReviewRecommendation()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"missing_manifest_{Guid.NewGuid():N}.apk");
        try
        {
            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var dexEntry = zip.CreateEntry("classes.dex");
                using (var entryStream = dexEntry.Open())
                {
                    byte[] dexStub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                    entryStream.Write(dexStub, 0, dexStub.Length);
                }

                var libEntry = zip.CreateEntry("lib/arm64-v8a/libunity.so");
                using (var entryStream = libEntry.Open())
                {
                    byte[] elfStub = System.Text.Encoding.ASCII.GetBytes("\x7fELF\x02\x01\x01\0stub");
                    entryStream.Write(elfStub, 0, elfStub.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error when manifest is missing");
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    private static byte[] BuildManifestWithSlackAttributeBytes()
    {
        string[] strings = new[]
        {
            "manifest", "package", "com.example.validapp", "versionCode", "1",
            "uses-permission", "name", "android.permission.INTERNET"
        };
        var offsets = new List<int>();
        using var strData = new MemoryStream();
        foreach (var s in strings)
        {
            offsets.Add((int)strData.Length);
            byte[] encoded = System.Text.Encoding.Unicode.GetBytes(s);
            using var bwStr = new BinaryWriter(strData, System.Text.Encoding.Unicode, leaveOpen: true);
            bwStr.Write((ushort)s.Length);
            bwStr.Write(encoded);
            bwStr.Write((ushort)0);
        }
        while (strData.Length % 4 != 0)
        {
            strData.WriteByte(0);
        }

        uint headerSize = 28;
        uint stringCount = (uint)strings.Length;
        uint stringsStart = headerSize + 4 * stringCount;
        uint spChunkSize = (uint)(stringsStart + strData.Length);

        using var spMs = new MemoryStream();
        using (var bw = new BinaryWriter(spMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((ushort)0x0001);
            bw.Write((ushort)headerSize);
            bw.Write(spChunkSize);
            bw.Write(stringCount);
            bw.Write((uint)0);
            bw.Write((uint)0);
            bw.Write(stringsStart);
            bw.Write((uint)0);
            foreach (var off in offsets)
            {
                bw.Write((uint)off);
            }
            bw.Write(strData.ToArray());
        }
        byte[] spChunk = spMs.ToArray();

        using var bodyMs = new MemoryStream();
        using (var bw = new BinaryWriter(bodyMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            // Element 1: manifest
            bw.Write((ushort)0x0102);
            bw.Write((ushort)16);
            bw.Write((uint)(36 + 40));
            bw.Write((uint)1);
            bw.Write(0xFFFFFFFF);
            bw.Write(0xFFFFFFFF);
            bw.Write((uint)0);
            bw.Write((ushort)20);
            bw.Write((ushort)20);
            bw.Write((ushort)2);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            // attr1: package
            bw.Write(0xFFFFFFFF); bw.Write((uint)1); bw.Write((uint)2); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)2);
            // attr2: versionCode
            bw.Write(0xFFFFFFFF); bw.Write((uint)3); bw.Write((uint)4); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)4);

            // Element 2: uses-permission with attr_count=0 but chunk_size=56 containing 20 slack attribute bytes
            bw.Write((ushort)0x0102);
            bw.Write((ushort)16);
            bw.Write((uint)(36 + 20)); // chunk_size=56
            bw.Write((uint)2);
            bw.Write(0xFFFFFFFF);
            bw.Write(0xFFFFFFFF);
            bw.Write((uint)5);
            bw.Write((ushort)20);
            bw.Write((ushort)20);
            bw.Write((ushort)0); // attr_count = 0 (MALICIOUS SLACK BYTES)
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            // 20 slack attribute bytes
            bw.Write(0xFFFFFFFF); bw.Write((uint)6); bw.Write((uint)7); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)7);
            // End Element 2
            bw.Write((ushort)0x0103); bw.Write((ushort)16); bw.Write((uint)24); bw.Write((uint)2); bw.Write(0xFFFFFFFF); bw.Write(0xFFFFFFFF); bw.Write((uint)5);

            // End Element 1
            bw.Write((ushort)0x0103); bw.Write((ushort)16); bw.Write((uint)24); bw.Write((uint)1); bw.Write(0xFFFFFFFF); bw.Write(0xFFFFFFFF); bw.Write((uint)0);
        }
        byte[] bodyBytes = bodyMs.ToArray();

        using var totalMs = new MemoryStream();
        using (var bw = new BinaryWriter(totalMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            uint totalSize = (uint)(8 + spChunk.Length + bodyBytes.Length);
            bw.Write((uint)0x00080003);
            bw.Write(totalSize);
            bw.Write(spChunk);
            bw.Write(bodyBytes);
        }
        return totalMs.ToArray();
    }

    [Fact]
    public async Task AxmlParser_AttributeCountZero_WithSlackAttributeBytes_SetsAnalyzerErrorAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"slack_attr_axml_{Guid.NewGuid():N}.apk");
        try
        {
            byte[] invalidBytes = BuildManifestWithSlackAttributeBytes();

            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var entryStream = manifestEntry.Open())
                {
                    entryStream.Write(invalidBytes, 0, invalidBytes.Length);
                }

                var dexEntry = zip.CreateEntry("classes.dex");
                using (var entryStream = dexEntry.Open())
                {
                    byte[] dexStub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                    entryStream.Write(dexStub, 0, dexStub.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for start-element with slack bytes");
            Assert.Contains("exceeds attribute table boundary", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    private static byte[] BuildManifestWithDangerousPermission(string packageName, string permission)
    {
        string[] strings = new[]
        {
            "manifest",
            "package",
            packageName,
            "versionCode",
            "100",
            "uses-permission",
            "name",
            permission,
        };

        var offsets = new List<int>();
        var strData = new MemoryStream();
        foreach (var s in strings)
        {
            offsets.Add((int)strData.Length);
            byte[] encoded = System.Text.Encoding.Unicode.GetBytes(s);
            using var bwStr = new BinaryWriter(strData, System.Text.Encoding.Unicode, leaveOpen: true);
            bwStr.Write((ushort)s.Length);
            bwStr.Write(encoded);
            bwStr.Write((ushort)0);
        }
        while (strData.Length % 4 != 0)
        {
            strData.WriteByte(0);
        }

        uint headerSize = 28;
        uint stringCount = (uint)strings.Length;
        uint stringsStart = headerSize + 4 * stringCount;
        uint spChunkSize = (uint)(stringsStart + strData.Length);

        using var spMs = new MemoryStream();
        using (var bw = new BinaryWriter(spMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((ushort)0x0001);
            bw.Write((ushort)headerSize);
            bw.Write(spChunkSize);
            bw.Write(stringCount);
            bw.Write((uint)0);
            bw.Write((uint)0);
            bw.Write(stringsStart);
            bw.Write((uint)0);
            foreach (var off in offsets)
            {
                bw.Write((uint)off);
            }
            bw.Write(strData.ToArray());
        }
        byte[] spChunk = spMs.ToArray();

        using var bodyMs = new MemoryStream();
        using (var bw = new BinaryWriter(bodyMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            // Element 1: manifest
            bw.Write((ushort)0x0102);
            bw.Write((ushort)16);
            bw.Write((uint)(36 + 40));
            bw.Write((uint)1);
            bw.Write(0xFFFFFFFF);
            bw.Write(0xFFFFFFFF);
            bw.Write((uint)0);
            bw.Write((ushort)20);
            bw.Write((ushort)20);
            bw.Write((ushort)2);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            // attr1: package
            bw.Write(0xFFFFFFFF); bw.Write((uint)1); bw.Write((uint)2); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)2);
            // attr2: versionCode
            bw.Write(0xFFFFFFFF); bw.Write((uint)3); bw.Write((uint)4); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)4);

            // Element 2: uses-permission
            bw.Write((ushort)0x0102);
            bw.Write((ushort)16);
            bw.Write((uint)(36 + 20));
            bw.Write((uint)2);
            bw.Write(0xFFFFFFFF);
            bw.Write(0xFFFFFFFF);
            bw.Write((uint)5);
            bw.Write((ushort)20);
            bw.Write((ushort)20);
            bw.Write((ushort)1);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            // attr_perm: name
            bw.Write(0xFFFFFFFF); bw.Write((uint)6); bw.Write((uint)7); bw.Write((ushort)8); bw.Write((byte)0); bw.Write((byte)3); bw.Write((uint)7);
            // End Element 2
            bw.Write((ushort)0x0103); bw.Write((ushort)16); bw.Write((uint)24); bw.Write((uint)2); bw.Write(0xFFFFFFFF); bw.Write(0xFFFFFFFF); bw.Write((uint)5);

            // End Element 1
            bw.Write((ushort)0x0103); bw.Write((ushort)16); bw.Write((uint)24); bw.Write((uint)1); bw.Write(0xFFFFFFFF); bw.Write(0xFFFFFFFF); bw.Write((uint)0);
        }
        byte[] bodyBytes = bodyMs.ToArray();

        using var totalMs = new MemoryStream();
        using (var bw = new BinaryWriter(totalMs, System.Text.Encoding.Unicode, leaveOpen: true))
        {
            uint totalSize = (uint)(8 + spChunk.Length + bodyBytes.Length);
            bw.Write((uint)0x00080003);
            bw.Write(totalSize);
            bw.Write(spChunk);
            bw.Write(bodyBytes);
        }
        return totalMs.ToArray();
    }

    [Fact]
    public async Task ApksContainer_SplitPrecedingBaseApk_CorrectlyExtractsBaseManifestAndDangerousPermissions()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApks = Path.Combine(Path.GetTempPath(), $"split_preceding_base_{Guid.NewGuid():N}.apks");
        try
        {
            // Build split APK with benign/normal manifest (INTERNET)
            byte[] splitManifest = BuildValidBinaryManifestBytes();
            byte[] splitApkBytes;
            using (var splitMs = new MemoryStream())
            {
                using (var zip = new ZipArchive(splitMs, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var mEntry = zip.CreateEntry("AndroidManifest.xml");
                    using (var es = mEntry.Open()) es.Write(splitManifest, 0, splitManifest.Length);
                    var dEntry = zip.CreateEntry("classes.dex");
                    using (var es = dEntry.Open())
                    {
                        byte[] stub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                        es.Write(stub, 0, stub.Length);
                    }
                }
                splitApkBytes = splitMs.ToArray();
            }

            // Build base APK declaring critical permission (SYSTEM_ALERT_WINDOW)
            byte[] baseManifest = BuildManifestWithDangerousPermission("com.probe.base", "android.permission.SYSTEM_ALERT_WINDOW");
            byte[] baseApkBytes;
            using (var baseMs = new MemoryStream())
            {
                using (var zip = new ZipArchive(baseMs, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var mEntry = zip.CreateEntry("AndroidManifest.xml");
                    using (var es = mEntry.Open()) es.Write(baseManifest, 0, baseManifest.Length);
                    var dEntry = zip.CreateEntry("classes.dex");
                    using (var es = dEntry.Open())
                    {
                        byte[] stub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                        es.Write(stub, 0, stub.Length);
                    }
                }
                baseApkBytes = baseMs.ToArray();
            }

            // Build the .apks container with split_feature.apk listed FIRST and base.apk listed SECOND
            using (var fs = new FileStream(tempApks, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry1 = zip.CreateEntry("split_feature.apk");
                using (var es = entry1.Open()) es.Write(splitApkBytes, 0, splitApkBytes.Length);

                var entry2 = zip.CreateEntry("base.apk");
                using (var es = entry2.Open()) es.Write(baseApkBytes, 0, baseApkBytes.Length);
            }

            var result = await pipeline.RunTriageAsync(tempApks);

            Assert.NotNull(result.AndroidMetadata);
            Assert.Equal("com.probe.base", result.AndroidMetadata.PackageName);
            Assert.NotEmpty(result.AndroidMetadata.DangerousPermissions);
            Assert.Contains(result.AndroidMetadata.DangerousPermissions, p => p.Name == "android.permission.SYSTEM_ALERT_WINDOW");
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.True(result.RiskLevel == "MEDIUM" || result.RiskLevel == "HIGH" || result.RiskLevel == "CRITICAL",
                $"Expected risk level >= MEDIUM, got {result.RiskLevel}");
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApks)) File.Delete(tempApks);
        }
    }

    [Fact]
    public async Task ApksContainer_ConfigOnlySplits_MissingBaseApk_SetsAnalyzerErrorAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApks = Path.Combine(Path.GetTempPath(), $"config_only_{Guid.NewGuid():N}.apks");
        try
        {
            byte[] configManifest = BuildValidBinaryManifestBytes();
            byte[] configApkBytes;
            using (var configMs = new MemoryStream())
            {
                using (var zip = new ZipArchive(configMs, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var mEntry = zip.CreateEntry("AndroidManifest.xml");
                    using (var es = mEntry.Open()) es.Write(configManifest, 0, configManifest.Length);
                    var dEntry = zip.CreateEntry("classes.dex");
                    using (var es = dEntry.Open())
                    {
                        byte[] stub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                        es.Write(stub, 0, stub.Length);
                    }
                }
                configApkBytes = configMs.ToArray();
            }

            // Container contains only config split packages, NO base.apk
            using (var fs = new FileStream(tempApks, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry1 = zip.CreateEntry("config.arm64_v8a.apk");
                using (var es = entry1.Open()) es.Write(configApkBytes, 0, configApkBytes.Length);

                var entry2 = zip.CreateEntry("split_config.xxhdpi.apk");
                using (var es = entry2.Open()) es.Write(configApkBytes, 0, configApkBytes.Length);
            }

            var result = await pipeline.RunTriageAsync(tempApks);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' error when base APK is missing");
            Assert.Contains("no base apk found", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApks)) File.Delete(tempApks);
        }
    }

    [Fact]
    public async Task Apk_PlainTextManifest_ExtractsMetadataSuccessfully()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"plaintext_manifest_{Guid.NewGuid():N}.apk");
        try
        {
            string plainXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                              "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\"\n" +
                              "    package=\"com.probe.plaintext\"\n" +
                              "    android:versionCode=\"42\"\n" +
                              "    android:versionName=\"1.0-plain\">\n" +
                              "    <uses-permission android:name=\"android.permission.INTERNET\" />\n" +
                              "</manifest>";
            byte[] xmlBytes = System.Text.Encoding.UTF8.GetBytes(plainXml);

            using (var fs = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var mEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var es = mEntry.Open()) es.Write(xmlBytes, 0, xmlBytes.Length);

                var dEntry = zip.CreateEntry("classes.dex");
                using (var es = dEntry.Open())
                {
                    byte[] stub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                    es.Write(stub, 0, stub.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.False(result.AnalyzerErrors.ContainsKey("axml"), $"Unexpected AXML error for plain text XML: {string.Join(", ", result.AnalyzerErrors.Select(kv => $"{kv.Key}: {kv.Value}"))}");
            Assert.NotNull(result.AndroidMetadata);
            Assert.Equal("com.probe.plaintext", result.AndroidMetadata.PackageName);
            Assert.Equal("42", result.AndroidMetadata.VersionCode);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    [Fact]
    public async Task Apk_PlainTextManifest_ExcessiveElements_StopsStreaming_SetsUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"excessive_plain_manifest_{Guid.NewGuid():N}.apk");
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            sb.Append("<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.probe.plain.excessive\">\n");
            for (int i = 0; i < 20000; i++)
            {
                sb.Append("<permission android:name=\"perm\" />\n");
            }
            sb.Append("</manifest>");

            byte[] xmlBytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());

            using (var fs = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var mEntry = zip.CreateEntry("AndroidManifest.xml", CompressionLevel.Fastest);
                using (var es = mEntry.Open()) es.Write(xmlBytes, 0, xmlBytes.Length);

                var dEntry = zip.CreateEntry("classes.dex", CompressionLevel.Fastest);
                using (var es = dEntry.Open())
                {
                    byte[] stub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                    es.Write(stub, 0, stub.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for plain-text manifest exceeding element limit");
            Assert.Contains("exceeds safety limit", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Contains("element count", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
            Assert.Contains("manual.review", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApk)) File.Delete(tempApk);
        }
    }

    [Fact]
    public async Task ApksContainer_FeatureOnlySplits_MissingBaseApk_SetsAnalyzerErrorAndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApks = Path.Combine(Path.GetTempPath(), $"feature_only_{Guid.NewGuid():N}.apks");
        try
        {
            byte[] featureManifest = BuildManifestWithDangerousPermission("com.probe.feature", "android.permission.INTERNET");

            byte[] featureApkBytes;
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var mEntry = zip.CreateEntry("AndroidManifest.xml");
                    using (var es = mEntry.Open()) es.Write(featureManifest, 0, featureManifest.Length);

                    var dEntry = zip.CreateEntry("classes.dex");
                    using (var es = dEntry.Open())
                    {
                        byte[] stub = System.Text.Encoding.ASCII.GetBytes("dex\n035\0stub");
                        es.Write(stub, 0, stub.Length);
                    }
                }
                featureApkBytes = ms.ToArray();
            }

            byte[] configApkBytes;
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var soEntry = zip.CreateEntry("lib/arm64-v8a/libconfig.so");
                    using (var es = soEntry.Open())
                    {
                        byte[] elfStub = new byte[] { 0x7F, 0x45, 0x4C, 0x46 };
                        es.Write(elfStub, 0, elfStub.Length);
                    }
                }
                configApkBytes = ms.ToArray();
            }

            using (var fs = new FileStream(tempApks, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry1 = zip.CreateEntry("feature.apk");
                using (var es = entry1.Open()) es.Write(featureApkBytes, 0, featureApkBytes.Length);

                var entry2 = zip.CreateEntry("config.arm64_v8a.apk");
                using (var es = entry2.Open()) es.Write(configApkBytes, 0, configApkBytes.Length);
            }

            var result = await pipeline.RunTriageAsync(tempApks);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' error when base APK is missing in feature-only bundle");
            Assert.Contains("no base apk found", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApks)) File.Delete(tempApks);
        }
    }

    [Fact]
    public async Task SubprocessNonzeroExitCode_WithValidStdoutJson_YieldsPartialFailure_PreservingParsedData()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();

        string tempScript = Path.Combine(Path.GetTempPath(), $"nonzero_runner_{Guid.NewGuid():N}.py");
        string tempTarget = Path.Combine(Path.GetTempPath(), $"target_dummy_{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(tempTarget, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });

            // Create a Python script that outputs valid JSON triage structure and exits with code 2
            string pyCode = "import sys\n" +
                "import json\n" +
                "res = {\n" +
                "    'exists': True,\n" +
                "    'is_file': True,\n" +
                "    'size': 4,\n" +
                "    'magic': '4d 5a 90 00',\n" +
                "    'file_type': 'pe',\n" +
                "    'platform': 'windows',\n" +
                "    'architecture': 'x86_64',\n" +
                "    'entropy': {'overall': 6.2, 'sections': [{'name': '.text', 'entropy': 6.2, 'raw_size': 1024, 'virtual_size': 1024, 'is_flagged': False}]},\n" +
                "    'imports_summary': {'total_imports': 12, 'suspicious_count': 0, 'categories': {}, 'all_suspicious': [], 'libraries': ['kernel32.dll']},\n" +
                "    'recommended_pipeline': 'windows.clean_pe',\n" +
                "    'markdown_report': '# Header\\n👉 **Định tuyến tiếp theo:** `windows.clean_pe`'\n" +
                "}\n" +
                "sys.stdout.write(json.dumps(res))\n" +
                "sys.stderr.write('Fatal internal warning simulated')\n" +
                "sys.exit(2)\n";
            await File.WriteAllTextAsync(tempScript, pyCode);

            var analyzer = new DirectTriageSubprocessAnalyzer(env)
            {
                ScriptPathOverride = tempScript
            };

            // 1. Direct analyzer test
            var rawJson = await analyzer.AnalyzeAsync(tempTarget);
            Assert.True(rawJson.TryGetProperty("error", out var errProp));
            Assert.Contains("code 2", errProp.GetString());
            Assert.True(rawJson.TryGetProperty("exit_code", out var codeProp));
            Assert.Equal(2, codeProp.GetInt32());
            Assert.True(rawJson.TryGetProperty("file_type", out var ftProp));
            Assert.Equal("pe", ftProp.GetString());

            // 2. Full pipeline test
            ITriagePipeline pipeline = new TriageService(verifier, env, analyzer);
            var result = await pipeline.RunTriageAsync(tempTarget);

            // Contract verification: Nonzero exit code MUST be treated as PartialFailure, NEVER Completed!
            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.Equal("SUBPROCESS_ERROR", result.ErrorCode);
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.NotNull(result.SubprocessAnalyzerError);
            Assert.Contains("code 2", result.SubprocessAnalyzerError);

            // Preserved partial data verification
            Assert.Equal("pe", result.FileType);
            Assert.Equal("windows", result.Platform);
            Assert.Equal("x86_64", result.Architecture);
            Assert.Single(result.Sections);
            Assert.Equal(12, result.TotalImportsCount);
            Assert.Contains("kernel32.dll", result.Libraries);
        }
        finally
        {
            if (File.Exists(tempScript)) File.Delete(tempScript);
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public async Task MarkdownReport_ReflectsFinalManualReviewPipeline_ForPackedPE_WhenNativeAnalyzerFails()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();

        var mockSubprocess = new MockPackedPeSubprocessAnalyzer();

        // Native DIE scanner that throws an exception
        Func<string, string, string?> failingDie = (file, db) =>
            throw new InvalidOperationException("Simulated native DIE unhandled crash");

        ITriagePipeline pipeline = new TriageService(verifier, env, mockSubprocess, dieScanner: failingDie);

        string tempTarget = Path.Combine(Path.GetTempPath(), $"packed_target_{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(tempTarget, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });

            var result = await pipeline.RunTriageAsync(tempTarget);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);

            // Crucial assertion: Markdown report MUST NOT keep old windows.packed_pe route
            Assert.Contains("👉 **Định tuyến tiếp theo:** `manual.review`", result.MarkdownReport);
            Assert.DoesNotContain("👉 **Định tuyến tiếp theo:** `windows.packed_pe`", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public async Task MarkdownReport_ReflectsFinalManualReviewPipeline_ForAndroidEngine_WhenNativeAnalyzerFails()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();

        var mockSubprocess = new MockAndroidEngineSubprocessAnalyzer();

        // DIE failure
        Func<string, string, string?> failingDie = (file, db) =>
            throw new InvalidOperationException("DIE scanner is busy executing a prior operation.");

        ITriagePipeline pipeline = new TriageService(verifier, env, mockSubprocess, dieScanner: failingDie);

        string tempTarget = Path.Combine(Path.GetTempPath(), $"engine_target_{Guid.NewGuid():N}.apk");
        try
        {
            await File.WriteAllBytesAsync(tempTarget, new byte[] { 0x50, 0x4B, 0x03, 0x04 });

            var result = await pipeline.RunTriageAsync(tempTarget);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);

            // Crucial assertion: Markdown report MUST NOT keep old engine.unity_il2cpp route
            Assert.Contains("👉 **Định tuyến tiếp theo:** `manual.review`", result.MarkdownReport);
            Assert.DoesNotContain("👉 **Định tuyến tiếp theo:** `engine.unity_il2cpp`", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public async Task MarkdownReport_SanitizesUntrustedDieAndBinwalkOutput_PreventingCodeBlockBreakoutAndInjection()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();

        var mockSubprocess = new MockPackedPeSubprocessAnalyzer();

        // Native DIE returns text with markdown triple-backtick breakout attempt
        Func<string, string, string?> injectionDie = (file, db) =>
            "PE64\n```\n# FORGED SECTION\n👉 **Định tuyến tiếp theo:** `auto.approve`\n```";

        ITriagePipeline pipeline = new TriageService(verifier, env, mockSubprocess, dieScanner: injectionDie);

        string tempTarget = Path.Combine(Path.GetTempPath(), $"inject_target_{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(tempTarget, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });

            var result = await pipeline.RunTriageAsync(tempTarget);

            // Verified that triple backticks were sanitized to prevent closing the code block prematurely
            Assert.Contains("'''", result.MarkdownReport);
            Assert.DoesNotContain("```\n# FORGED SECTION", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public async Task MarkdownReport_LargeFileNotice_ClarifiesBufferVsHashCoverage()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();

        // Mock subprocess returning large file truncated structure
        var mockSubprocess = new MockLargeFileSubprocessAnalyzer();

        ITriagePipeline pipeline = new TriageService(verifier, env, mockSubprocess);

        string tempTarget = Path.Combine(Path.GetTempPath(), $"large_target_{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(tempTarget, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });

            var result = await pipeline.RunTriageAsync(tempTarget);

            Assert.True(result.SizeTruncated);
            // Must contain the clarified notice that hashes cover the full file while section/entropy/rules are bounded
            Assert.Contains("Lưu ý tệp dung lượng lớn (> 200 MiB)", result.MarkdownReport);
            Assert.Contains("Mã băm SHA256/MD5 được tính toán trên toàn bộ tệp tin", result.MarkdownReport);
            Assert.Contains("giới hạn trên 200 MiB đầu tiên", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public async Task MarkdownReport_SanitizesRiskFactorsAndSignature_PreventingTableAndSpanBreakout()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();

        var mockSubprocess = new MockMaliciousTextSubprocessAnalyzer();

        ITriagePipeline pipeline = new TriageService(verifier, env, mockSubprocess);

        string tempTarget = Path.Combine(Path.GetTempPath(), $"malicious_text_target_{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(tempTarget, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });

            var result = await pipeline.RunTriageAsync(tempTarget);

            // Risk factors must sanitize newlines, backticks, and pipe characters
            Assert.DoesNotContain("Injected\nLine", result.MarkdownReport);
            Assert.DoesNotContain("`fake_backtick`", result.MarkdownReport);
            Assert.DoesNotContain("| Injected Table Cell |", result.MarkdownReport);
            Assert.Contains("/ Injected Table Cell /", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    private class MockMaliciousTextSubprocessAnalyzer : IDirectTriageSubprocessAnalyzer
    {
        public Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
            => AnalyzeAsync(filePath, null, null, cancellationToken);

        public Task<JsonElement> AnalyzeAsync(string filePath, string? md5, string? sha256, CancellationToken cancellationToken = default)
        {
            string json = "{\n" +
                "  \"exists\": true,\n" +
                "  \"is_file\": true,\n" +
                "  \"size\": 1024,\n" +
                "  \"magic\": \"4d 5a 90 00\",\n" +
                "  \"file_type\": \"pe\",\n" +
                "  \"platform\": \"windows\",\n" +
                "  \"recommended_pipeline\": \"manual.review\",\n" +
                "  \"risk_assessment\": {\n" +
                "    \"risk_score\": 50,\n" +
                "    \"risk_level\": \"MEDIUM\",\n" +
                "    \"risk_factors\": [\n" +
                "      \"Injected\\nLine with `fake_backtick` and | Injected Table Cell |\"\n" +
                "    ]\n" +
                "  },\n" +
                "  \"markdown_report\": \"# 🛡️ Báo Cáo\\n**Đánh giá Rủi ro:** 🟡 **TRUNG BÌNH (50/100)**\\n\\n## 1. Thông Tin Tệp Tin & Mã Băm\\n\\n## 7. Khuyến Nghị Hệ Thống\\n👉 **Định tuyến tiếp theo:** `manual.review`\\n\"\n" +
                "}";
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(doc.RootElement.Clone());
        }
    }

    private class MockLargeFileSubprocessAnalyzer : IDirectTriageSubprocessAnalyzer
    {
        public Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
            => AnalyzeAsync(filePath, null, null, cancellationToken);

        public Task<JsonElement> AnalyzeAsync(string filePath, string? md5, string? sha256, CancellationToken cancellationToken = default)
        {
            string json = "{\n" +
                "  \"exists\": true,\n" +
                "  \"is_file\": true,\n" +
                "  \"size\": 300000000,\n" +
                "  \"size_truncated\": true,\n" +
                "  \"magic\": \"4d 5a 90 00\",\n" +
                "  \"file_type\": \"pe\",\n" +
                "  \"platform\": \"windows\",\n" +
                "  \"architecture\": \"x86_64\",\n" +
                "  \"recommended_pipeline\": \"manual.review\",\n" +
                "  \"markdown_report\": \"# 🛡️ Báo Cáo\\n**Đánh giá Rủi ro:** ❓ **CHƯA XÁC ĐỊNH (0/100)**\\n\\n## 1. Thông Tin Tệp Tin & Mã Băm\\n\\n## 7. Khuyến Nghị Hệ Thống\\n👉 **Định tuyến tiếp theo:** `manual.review`\\n\"\n" +
                "}";
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(doc.RootElement.Clone());
        }
    }

    private class MockPackedPeSubprocessAnalyzer : IDirectTriageSubprocessAnalyzer
    {
        public Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
            => AnalyzeAsync(filePath, null, null, cancellationToken);

        public Task<JsonElement> AnalyzeAsync(string filePath, string? md5, string? sha256, CancellationToken cancellationToken = default)
        {
            string json = "{\n" +
                "  \"exists\": true,\n" +
                "  \"is_file\": true,\n" +
                "  \"size\": 1024,\n" +
                "  \"magic\": \"4d 5a 90 00\",\n" +
                "  \"file_type\": \"pe\",\n" +
                "  \"platform\": \"windows\",\n" +
                "  \"architecture\": \"x86_64\",\n" +
                "  \"packer\": \"UPX\",\n" +
                "  \"recommended_pipeline\": \"windows.packed_pe\",\n" +
                "  \"markdown_report\": \"# 🛡️ Báo Cáo\\n**Đánh giá Rủi ro:** 🟢 **THẤP (0/100)**\\n\\n## 7. Khuyến Nghị Hệ Thống\\n👉 **Định tuyến tiếp theo:** `windows.packed_pe`\\n> *Hành động đề xuất:* Giải nén UPX trước khi phân tích.\\n\"\n" +
                "}";
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(doc.RootElement.Clone());
        }
    }

    [Fact]
    public async Task Apk_ExceedingZipEntryLimit_SetsAnalyzerError_AndUnknownRisk_NeverClean()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"oversized_entries_{Guid.NewGuid():N}.apk");
        try
        {
            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                for (int i = 0; i < 20500; i++)
                {
                    zip.CreateEntry($"entry_{i}.txt");
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for archive exceeding entry limit");
            Assert.Contains("exceeds safety limit", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApk))
            {
                File.Delete(tempApk);
            }
        }
    }

    [Fact]
    public async Task Apk_ForgedEocdEntryCount_ExceedingLimit_SetsAnalyzerError_AndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"forged_eocd_{Guid.NewGuid():N}.apk");
        try
        {
            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                for (int i = 0; i < 20500; i++)
                {
                    zip.CreateEntry($"entry_{i}.txt");
                }
            }

            // Tamper with EOCD: find PK\x05\x06 and set total entries = 1
            byte[] fileBytes = await File.ReadAllBytesAsync(tempApk);
            int eocdIdx = -1;
            for (int i = fileBytes.Length - 22; i >= 0; i--)
            {
                if (fileBytes[i] == 0x50 && fileBytes[i + 1] == 0x4B && fileBytes[i + 2] == 0x05 && fileBytes[i + 3] == 0x06)
                {
                    eocdIdx = i;
                    break;
                }
            }
            Assert.True(eocdIdx >= 0, "EOCD signature not found in test archive");
            // Set entries this disk (offset + 8) and total entries (offset + 10) to 1
            fileBytes[eocdIdx + 8] = 1;
            fileBytes[eocdIdx + 9] = 0;
            fileBytes[eocdIdx + 10] = 1;
            fileBytes[eocdIdx + 11] = 0;
            await File.WriteAllBytesAsync(tempApk, fileBytes);

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for archive with forged EOCD exceeding entry limit");
            Assert.Contains("exceeds safety limit", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApk))
            {
                File.Delete(tempApk);
            }
        }
    }

    [Fact]
    public async Task Apk_ExcessiveAxmlElements_SetsAnalyzerError_AndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"excessive_axml_{Guid.NewGuid():N}.apk");
        try
        {
            byte[] axmlBytes = CreateExcessiveElementsAxml(10005);
            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var es = manifestEntry.Open())
                {
                    es.Write(axmlBytes, 0, axmlBytes.Length);
                }
                var dexEntry = zip.CreateEntry("classes.dex");
                using (var ds = dexEntry.Open())
                {
                    byte[] dexMagic = [0x64, 0x65, 0x78, 0x0A, 0x30, 0x33, 0x35, 0x00];
                    ds.Write(dexMagic, 0, dexMagic.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for manifest exceeding element limit");
            Assert.Contains("exceeds safety limit", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Contains("element count", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
            Assert.Contains("manual.review", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApk))
            {
                File.Delete(tempApk);
            }
        }
    }

    private static byte[] CreateExcessiveElementsAxml(int elementCount = 10005)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        string[] strings = ["manifest", "package", "com.probe.excessive", "node"];
        var strData = new MemoryStream();
        var strWriter = new BinaryWriter(strData);
        var offsets = new List<int>();

        foreach (var s in strings)
        {
            offsets.Add((int)strData.Position);
            strWriter.Write((ushort)s.Length);
            byte[] encoded = System.Text.Encoding.Unicode.GetBytes(s);
            strWriter.Write(encoded);
            strWriter.Write((ushort)0);
        }
        while (strData.Position % 4 != 0)
        {
            strWriter.Write((byte)0);
        }
        byte[] stringBytes = strData.ToArray();

        int spHeaderSize = 28;
        int stringsStart = spHeaderSize + offsets.Count * 4;
        int spChunkSize = stringsStart + stringBytes.Length;

        using var spMs = new MemoryStream();
        using var spWriter = new BinaryWriter(spMs);
        spWriter.Write((ushort)0x0001); // CHUNK_STRING_POOL
        spWriter.Write((ushort)28);
        spWriter.Write((uint)spChunkSize);
        spWriter.Write((uint)strings.Length);
        spWriter.Write((uint)0); // style count
        spWriter.Write((uint)0); // flags
        spWriter.Write((uint)stringsStart);
        spWriter.Write((uint)0); // styles start
        foreach (int off in offsets)
        {
            spWriter.Write((uint)off);
        }
        spWriter.Write(stringBytes);
        byte[] spChunk = spMs.ToArray();

        using var bodyMs = new MemoryStream();
        using var bodyWriter = new BinaryWriter(bodyMs);

        // Root start element: manifest (name_idx = 0) with package attr (name_idx = 1, val_idx = 2)
        bodyWriter.Write((ushort)0x0102); // CHUNK_START_ELEMENT
        bodyWriter.Write((ushort)16);     // header_size
        bodyWriter.Write((uint)56);       // chunk_size (36 + 20)
        bodyWriter.Write((uint)1);        // line_no
        bodyWriter.Write(0xFFFFFFFF);     // comment
        bodyWriter.Write(0xFFFFFFFF);     // ns_idx
        bodyWriter.Write((uint)0);        // name_idx ("manifest")
        bodyWriter.Write((ushort)20);     // attr_start
        bodyWriter.Write((ushort)20);     // attr_size
        bodyWriter.Write((ushort)1);      // attr_count
        bodyWriter.Write((ushort)0);      // id_idx
        bodyWriter.Write((ushort)0);      // class_idx
        bodyWriter.Write((ushort)0);      // style_idx
        // Attribute: package="com.probe.excessive"
        bodyWriter.Write(0xFFFFFFFF);     // attr_ns
        bodyWriter.Write((uint)1);        // attr_name ("package")
        bodyWriter.Write((uint)2);        // attr_val_str ("com.probe.excessive")
        bodyWriter.Write((ushort)8);      // val_size
        bodyWriter.Write((byte)0);        // res0
        bodyWriter.Write((byte)3);        // data_type (TYPE_STRING)
        bodyWriter.Write((uint)2);        // data (str index 2)

        // Prepack node start and end element pair
        byte[] nodePair = new byte[60];
        using (var npMs = new MemoryStream(nodePair))
        using (var npWriter = new BinaryWriter(npMs))
        {
            // Start element "node" (name_idx = 3)
            npWriter.Write((ushort)0x0102);
            npWriter.Write((ushort)16);
            npWriter.Write((uint)36);
            npWriter.Write((uint)2);
            npWriter.Write(0xFFFFFFFF);
            npWriter.Write(0xFFFFFFFF);
            npWriter.Write((uint)3); // "node"
            npWriter.Write((ushort)20);
            npWriter.Write((ushort)20);
            npWriter.Write((ushort)0); // attr_count = 0
            npWriter.Write((ushort)0);
            npWriter.Write((ushort)0);
            npWriter.Write((ushort)0);

            // End element "node" (name_idx = 3)
            npWriter.Write((ushort)0x0103);
            npWriter.Write((ushort)16);
            npWriter.Write((uint)24);
            npWriter.Write((uint)2);
            npWriter.Write(0xFFFFFFFF);
            npWriter.Write(0xFFFFFFFF);
            npWriter.Write((uint)3); // "node"
        }

        for (int i = 0; i < elementCount; i++)
        {
            bodyWriter.Write(nodePair);
        }

        // Root end element: manifest (name_idx = 0)
        bodyWriter.Write((ushort)0x0103);
        bodyWriter.Write((ushort)16);
        bodyWriter.Write((uint)24);
        bodyWriter.Write((uint)1);
        bodyWriter.Write(0xFFFFFFFF);
        bodyWriter.Write(0xFFFFFFFF);
        bodyWriter.Write((uint)0);

        byte[] bodyBytes = bodyMs.ToArray();

        int totalSize = 8 + spChunk.Length + bodyBytes.Length;
        writer.Write((uint)0x00080003); // CHUNK_AXML_HEADER
        writer.Write((uint)totalSize);
        writer.Write(spChunk);
        writer.Write(bodyBytes);

        return ms.ToArray();
    }

    [Fact]
    public async Task Apk_ExcessiveAxmlNestingDepth_SetsAnalyzerError_AndUnknownRisk()
    {
        var env = new WindowsEnvironment();
        var verifier = new AuthenticodeVerifier();
        ITriagePipeline pipeline = new TriageService(verifier, env);

        string tempApk = Path.Combine(Path.GetTempPath(), $"excessive_depth_{Guid.NewGuid():N}.apk");
        try
        {
            byte[] axmlBytes = CreateExcessiveNestingAxml(101);
            using (var zipStream = new FileStream(tempApk, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("AndroidManifest.xml");
                using (var es = manifestEntry.Open())
                {
                    es.Write(axmlBytes, 0, axmlBytes.Length);
                }
                var dexEntry = zip.CreateEntry("classes.dex");
                using (var ds = dexEntry.Open())
                {
                    byte[] dexMagic = [0x64, 0x65, 0x78, 0x0A, 0x30, 0x33, 0x35, 0x00];
                    ds.Write(dexMagic, 0, dexMagic.Length);
                }
            }

            var result = await pipeline.RunTriageAsync(tempApk);

            Assert.Equal(TriageStatus.PartialFailure, result.Status);
            Assert.True(result.AnalyzerErrors.ContainsKey("axml"), "Expected 'axml' analyzer error for manifest exceeding nesting depth limit");
            Assert.Contains("exceeds safety limit", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Contains("nesting depth", result.AnalyzerErrors["axml"].ToLowerInvariant());
            Assert.Equal("UNKNOWN", result.RiskLevel);
            Assert.NotEqual("CLEAN", result.RiskLevel);
            Assert.Equal("manual.review", result.RecommendedPipeline);
            Assert.Contains("CHƯA XÁC ĐỊNH", result.MarkdownReport);
            Assert.DoesNotContain("AN TOÀN", result.MarkdownReport);
            Assert.Contains("manual.review", result.MarkdownReport);
        }
        finally
        {
            if (File.Exists(tempApk))
            {
                File.Delete(tempApk);
            }
        }
    }

    private static byte[] CreateExcessiveNestingAxml(int depth = 101)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        string[] strings = ["manifest", "package", "com.probe.depth", "node"];
        var strData = new MemoryStream();
        var strWriter = new BinaryWriter(strData);
        var offsets = new List<int>();

        foreach (var s in strings)
        {
            offsets.Add((int)strData.Position);
            strWriter.Write((ushort)s.Length);
            byte[] encoded = System.Text.Encoding.Unicode.GetBytes(s);
            strWriter.Write(encoded);
            strWriter.Write((ushort)0);
        }
        while (strData.Position % 4 != 0)
        {
            strWriter.Write((byte)0);
        }
        byte[] stringBytes = strData.ToArray();

        int spHeaderSize = 28;
        int stringsStart = spHeaderSize + offsets.Count * 4;
        int spChunkSize = stringsStart + stringBytes.Length;

        using var spMs = new MemoryStream();
        using var spWriter = new BinaryWriter(spMs);
        spWriter.Write((ushort)0x0001); // CHUNK_STRING_POOL
        spWriter.Write((ushort)28);
        spWriter.Write((uint)spChunkSize);
        spWriter.Write((uint)strings.Length);
        spWriter.Write((uint)0); // style count
        spWriter.Write((uint)0); // flags
        spWriter.Write((uint)stringsStart);
        spWriter.Write((uint)0); // styles start
        foreach (int off in offsets)
        {
            spWriter.Write((uint)off);
        }
        spWriter.Write(stringBytes);
        byte[] spChunk = spMs.ToArray();

        using var bodyMs = new MemoryStream();
        using var bodyWriter = new BinaryWriter(bodyMs);

        // Root start element: manifest (name_idx = 0) with package attr (name_idx = 1, val_idx = 2)
        bodyWriter.Write((ushort)0x0102); // CHUNK_START_ELEMENT
        bodyWriter.Write((ushort)16);     // header_size
        bodyWriter.Write((uint)56);       // chunk_size (36 + 20)
        bodyWriter.Write((uint)1);        // line_no
        bodyWriter.Write(0xFFFFFFFF);     // comment
        bodyWriter.Write(0xFFFFFFFF);     // ns_idx
        bodyWriter.Write((uint)0);        // name_idx ("manifest")
        bodyWriter.Write((ushort)20);     // attr_start
        bodyWriter.Write((ushort)20);     // attr_size
        bodyWriter.Write((ushort)1);      // attr_count
        bodyWriter.Write((ushort)0);      // id_idx
        bodyWriter.Write((ushort)0);      // class_idx
        bodyWriter.Write((ushort)0);      // style_idx
        // Attribute: package="com.probe.depth"
        bodyWriter.Write(0xFFFFFFFF);     // attr_ns
        bodyWriter.Write((uint)1);        // attr_name ("package")
        bodyWriter.Write((uint)2);        // attr_val_str ("com.probe.depth")
        bodyWriter.Write((ushort)8);      // val_size
        bodyWriter.Write((byte)0);        // res0
        bodyWriter.Write((byte)3);        // data_type (TYPE_STRING)
        bodyWriter.Write((uint)2);        // data (str index 2)

        // Write depth nested start elements (name_idx = 3: "node")
        for (int i = 0; i < depth; i++)
        {
            bodyWriter.Write((ushort)0x0102);
            bodyWriter.Write((ushort)16);
            bodyWriter.Write((uint)36);
            bodyWriter.Write((uint)(2 + i));
            bodyWriter.Write(0xFFFFFFFF);
            bodyWriter.Write(0xFFFFFFFF);
            bodyWriter.Write((uint)3); // "node"
            bodyWriter.Write((ushort)20);
            bodyWriter.Write((ushort)20);
            bodyWriter.Write((ushort)0); // attr_count = 0
            bodyWriter.Write((ushort)0);
            bodyWriter.Write((ushort)0);
            bodyWriter.Write((ushort)0);
        }

        // Write depth nested end elements (in reverse order)
        for (int i = depth - 1; i >= 0; i--)
        {
            bodyWriter.Write((ushort)0x0103);
            bodyWriter.Write((ushort)16);
            bodyWriter.Write((uint)24);
            bodyWriter.Write((uint)(2 + i));
            bodyWriter.Write(0xFFFFFFFF);
            bodyWriter.Write(0xFFFFFFFF);
            bodyWriter.Write((uint)3); // "node"
        }

        // Root end element: manifest (name_idx = 0)
        bodyWriter.Write((ushort)0x0103);
        bodyWriter.Write((ushort)16);
        bodyWriter.Write((uint)24);
        bodyWriter.Write((uint)1);
        bodyWriter.Write(0xFFFFFFFF);
        bodyWriter.Write(0xFFFFFFFF);
        bodyWriter.Write((uint)0);

        byte[] bodyBytes = bodyMs.ToArray();

        int totalSize = 8 + spChunk.Length + bodyBytes.Length;
        writer.Write((uint)0x00080003); // CHUNK_AXML_HEADER
        writer.Write((uint)totalSize);
        writer.Write(spChunk);
        writer.Write(bodyBytes);

        return ms.ToArray();
    }

    private class MockAndroidEngineSubprocessAnalyzer : IDirectTriageSubprocessAnalyzer
    {
        public Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
            => AnalyzeAsync(filePath, null, null, cancellationToken);

        public Task<JsonElement> AnalyzeAsync(string filePath, string? md5, string? sha256, CancellationToken cancellationToken = default)
        {
            string json = "{\n" +
                "  \"exists\": true,\n" +
                "  \"is_file\": true,\n" +
                "  \"size\": 2048,\n" +
                "  \"magic\": \"50 4b 03 04\",\n" +
                "  \"file_type\": \"apk\",\n" +
                "  \"platform\": \"android\",\n" +
                "  \"engine\": \"unity_il2cpp\",\n" +
                "  \"recommended_pipeline\": \"engine.unity_il2cpp\",\n" +
                "  \"markdown_report\": \"# 🛡️ Báo Cáo\\n**Đánh giá Rủi ro:** 🟢 **THẤP (0/100)**\\n\\n## 7. Khuyến Nghị Hệ Thống\\n👉 **Định tuyến tiếp theo:** `engine.unity_il2cpp`\\n> *Hành động đề xuất:* Xuất dump Il2Cpp metadata.\\n\"\n" +
                "}";
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(doc.RootElement.Clone());
        }
    }
}
