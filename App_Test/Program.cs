using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RE.Core.Abstractions;
using RE.Core.Services;
using RE.Core.StaticAnalysis;
using RE.Platform.Windows;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("===================================================================");
        Console.WriteLine(" RE ECOSYSTEM 2.0 - DETERMINISTIC TRIAGE TEST (Phase 1 Canonical) ");
        Console.WriteLine(" Zero LLM | Zero MCP | Direct Subprocess & Native P/Invoke        ");
        Console.WriteLine("===================================================================");

        var env = new WindowsEnvironment();
        var signatureVerifier = new AuthenticodeVerifier();

        string currentDir = AppDomain.CurrentDomain.BaseDirectory;
        string? root = currentDir;
        while (root != null && !Directory.Exists(Path.Combine(root, "Python_Runtime")))
        {
            root = Directory.GetParent(root)?.FullName;
        }

        // Initialize canonical ITriagePipeline without any MCP manager
        ITriagePipeline triageService = new TriageService(signatureVerifier, env);
#pragma warning disable CS0618
        TriageOrchestrator.Configure(triageService);
#pragma warning restore CS0618

        // 1. Test Windows PE Executable
        string targetFile = Path.Combine(root ?? "D:\\Projects\\ReverseEngineering\\RE_Ecosystem", @"Python_Runtime\python.exe");
        Console.WriteLine($"\n[+] Testing ITriagePipeline on Windows PE: {targetFile}");
        var peResult = await triageService.RunTriageAsync(targetFile);

        Console.WriteLine($"\n--- PE TRIAGE RESULT ---");
        Console.WriteLine($"File: {peResult.FileName} ({peResult.Size:N0} bytes)");
        Console.WriteLine($"Type: {peResult.FileType} | Platform: {peResult.Platform} | Arch: {peResult.Architecture}");
        Console.WriteLine($"MD5: {peResult.MD5Hash}");
        Console.WriteLine($"SHA256: {peResult.SHA256Hash}");
        Console.WriteLine($"Authenticode: {peResult.Signature.Status} ({peResult.Signature.Subject})");
        Console.WriteLine($"Sections Count: {peResult.Sections.Count}");
        Console.WriteLine($"Suspicious Imports: {peResult.SuspiciousImports.Count} / Total: {peResult.TotalImportsCount}");
        Console.WriteLine($"Risk: {peResult.RiskLevel} (Score: {peResult.RiskScore}/100)");
        Console.WriteLine($"Pipeline: {peResult.RecommendedPipeline}");

        // Assertions for PE Triage
        if (peResult.Size == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL] PE Triage returned size 0!");
            Console.ResetColor();
            return 1;
        }
        if (peResult.RecommendedPipeline == "error.triage_failed")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL] PE Triage failed with error.triage_failed!");
            Console.ResetColor();
            return 1;
        }
        if (peResult.FileType != "pe" || peResult.Platform != "windows")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] Unexpected PE classification: type={peResult.FileType}, platform={peResult.Platform}");
            Console.ResetColor();
            return 1;
        }
        if (string.IsNullOrEmpty(peResult.MD5Hash) || peResult.MD5Hash == "N/A")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL] Native MD5 hash is missing!");
            Console.ResetColor();
            return 1;
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[PASS] Windows PE Triage assertions passed!");
        Console.ResetColor();

        // 2. Test Android APK
        string testApkPath = Path.Combine(root ?? "D:\\Projects\\ReverseEngineering\\RE_Ecosystem", "temp_test_game.apk");
        try
        {
            using (var zip = ZipFile.Open(testApkPath, ZipArchiveMode.Create))
            {
                var dexEntry = zip.CreateEntry("classes.dex");
                using (var s = dexEntry.Open())
                {
                    byte[] dexHeader = System.Text.Encoding.ASCII.GetBytes("dex\n035\0");
                    s.Write(dexHeader, 0, dexHeader.Length);
                }

                var soEntry = zip.CreateEntry("lib/arm64-v8a/libil2cpp.so");
                using (var s = soEntry.Open())
                {
                    byte[] elfHeader = new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' };
                    s.Write(elfHeader, 0, elfHeader.Length);
                }

                var unitySoEntry = zip.CreateEntry("lib/arm64-v8a/libunity.so");
                using (var s = unitySoEntry.Open())
                {
                    byte[] elfHeader = new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' };
                    s.Write(elfHeader, 0, elfHeader.Length);
                }

                var metaEntry = zip.CreateEntry("assets/bin/Data/Managed/global-metadata.dat");
                using (var s = metaEntry.Open())
                {
                    byte[] dummy = System.Text.Encoding.UTF8.GetBytes("Unity Metadata");
                    s.Write(dummy, 0, dummy.Length);
                }
            }

            Console.WriteLine($"\n[+] Testing ITriagePipeline on Android APK: {testApkPath}");
            var apkResult = await triageService.RunTriageAsync(testApkPath);

            Console.WriteLine($"\n--- ANDROID TRIAGE RESULT ---");
            Console.WriteLine($"File: {apkResult.FileName} ({apkResult.Size:N0} bytes)");
            Console.WriteLine($"Type: {apkResult.FileType} | Platform: {apkResult.Platform} | Arch: {apkResult.Architecture}");
            Console.WriteLine($"Engine: {apkResult.Engine} | Packer: {apkResult.Packer}");
            Console.WriteLine($"Android Metadata present: {apkResult.AndroidMetadata != null}");
            if (apkResult.AndroidMetadata != null)
            {
                Console.WriteLine($"  - DEX Count: {apkResult.AndroidMetadata.DexCount}");
                Console.WriteLine($"  - Native Archs: {string.Join(", ", apkResult.AndroidMetadata.NativeArchitectures)}");
                Console.WriteLine($"  - Native Libs: {string.Join(", ", apkResult.AndroidMetadata.NativeLibraries)}");
            }
            Console.WriteLine($"Risk: {apkResult.RiskLevel} (Score: {apkResult.RiskScore}/100)");
            Console.WriteLine($"Pipeline: {apkResult.RecommendedPipeline}");
            Console.WriteLine($"Markdown Report Length: {apkResult.MarkdownReport.Length} chars");

            // Assertions for APK Triage
            if (apkResult.FileType != "apk" || apkResult.Platform != "android")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] Unexpected APK classification: type={apkResult.FileType}, platform={apkResult.Platform}");
                Console.ResetColor();
                return 1;
            }
            if (apkResult.Engine != "unity_il2cpp" || apkResult.RecommendedPipeline != "android.unity_il2cpp")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] Unexpected Engine / Pipeline recommendation: engine={apkResult.Engine}, pipeline={apkResult.RecommendedPipeline}");
                Console.ResetColor();
                return 1;
            }
            if (apkResult.AndroidMetadata == null || apkResult.AndroidMetadata.DexCount != 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Android metadata or DEX count incorrect!");
                Console.ResetColor();
                return 1;
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[PASS] Android APK Triage assertions passed!");
            Console.ResetColor();
        }
        finally
        {
            if (File.Exists(testApkPath))
            {
                File.Delete(testApkPath);
            }
        }

        // 3. Test Missing File handling
        Console.WriteLine("\n[+] Testing ITriagePipeline on non-existent file handling");
        var missingResult = await triageService.RunTriageAsync(Path.Combine(root ?? "D:\\Projects\\ReverseEngineering\\RE_Ecosystem", "does_not_exist_file.bin"));
        if (missingResult.RecommendedPipeline != "error.file_not_found")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] Expected error.file_not_found but got: {missingResult.RecommendedPipeline}");
            Console.ResetColor();
            return 1;
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[PASS] Non-existent file handling assertion passed!");
        Console.ResetColor();

        // 4. Test Cancellation Propagation
        Console.WriteLine("\n[+] Testing ITriagePipeline cancellation propagation");
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); // Pre-cancelled
            try
            {
                await triageService.RunTriageAsync(targetFile, cts.Token);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Expected OperationCanceledException but method completed!");
                Console.ResetColor();
                return 1;
            }
            catch (OperationCanceledException)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[PASS] Cancellation correctly propagated OperationCanceledException!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] Unexpected exception during cancellation: {ex.GetType().Name} - {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        // 5. Test Legacy MCP Triage Guard (Regression Prevention)
        Console.WriteLine("\n[+] Testing Legacy MCP 'static.triage' regression guard");
        var mcpManager = new McpClientManager(env);
        try
        {
            await mcpManager.GetClientAsync("static.triage");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL] McpClientManager allowed 'static.triage' without exception!");
            Console.ResetColor();
            return 1;
        }
        catch (NotSupportedException ex)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[PASS] McpClientManager blocked 'static.triage': {ex.Message}");
            Console.ResetColor();
        }

        try
        {
#pragma warning disable CS0618
            await mcpManager.CallToolAsync("triage_file", new { path = targetFile });
#pragma warning restore CS0618
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL] McpClientManager allowed parameterless CallToolAsync without exception!");
            Console.ResetColor();
            return 1;
        }
        catch (NotSupportedException)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[PASS] McpClientManager blocked legacy CallToolAsync(toolName, args)!");
            Console.ResetColor();
        }

        // 6. Test Composite Analyzer Partial Failure Isolation
        Console.WriteLine("\n[+] Testing Composite Analyzer partial failure handling");
        var failingAnalyzer = new FailingSubprocessAnalyzer();
        ITriagePipeline resilientPipeline = new TriageService(signatureVerifier, env, failingAnalyzer);
        var partialResult = await resilientPipeline.RunTriageAsync(targetFile);

        if (partialResult.RiskLevel == "CLEAN")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL] Partial failure should NOT be marked as CLEAN!");
            Console.ResetColor();
            return 1;
        }
        if (partialResult.RiskLevel != "UNKNOWN")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] Expected UNKNOWN risk level on core failure, got: {partialResult.RiskLevel}");
            Console.ResetColor();
            return 1;
        }
        if (string.IsNullOrEmpty(partialResult.MD5Hash) || partialResult.MD5Hash == "N/A")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL] Native hashes should be preserved despite subprocess analyzer failure!");
            Console.ResetColor();
            return 1;
        }
        if (partialResult.RiskFactors.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL] Subprocess failure error was not exposed in RiskFactors!");
            Console.ResetColor();
            return 1;
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[PASS] Partial failure correctly isolated, preserved hashes and exposed error without false CLEAN!");
        Console.ResetColor();

        // 7. Test TriageOrchestrator Delegation (Single Orchestrator Invariant)
        Console.WriteLine("\n[+] Testing TriageOrchestrator delegation to canonical pipeline");
#pragma warning disable CS0618
        string orchJson = await TriageOrchestrator.RunTriageAsync(targetFile);
#pragma warning restore CS0618
        if (string.IsNullOrEmpty(orchJson) || !orchJson.Contains("\"pe\"") || orchJson.Contains("\"error.triage_failed\""))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] TriageOrchestrator delegation returned invalid result: {orchJson}");
            Console.ResetColor();
            return 1;
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[PASS] TriageOrchestrator delegation succeeded!");
        Console.ResetColor();

        Console.WriteLine("\n===================================================================");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[OK] ALL CANONICAL DETERMINISTIC TRIAGE TESTS PASSED SUCCESSFULLY!");
        Console.ResetColor();
        Console.WriteLine("===================================================================");
        return 0;
    }
}

class FailingSubprocessAnalyzer : IDirectTriageSubprocessAnalyzer
{
    public Task<JsonElement> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var errObj = new Dictionary<string, object?>
        {
            ["exists"] = true,
            ["error"] = "Corrupted ELF/PE structure simulation",
            ["recommended_pipeline"] = "manual.review"
        };
        var json = JsonSerializer.Serialize(errObj);
        return Task.FromResult(JsonSerializer.Deserialize<JsonElement>(json));
    }
}
