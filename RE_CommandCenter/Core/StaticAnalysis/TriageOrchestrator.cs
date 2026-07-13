using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace RE_CommandCenter.Core.StaticAnalysis
{
    public static class TriageOrchestrator
    {
        private static McpClient? _mcpClient;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

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
            // Fallback
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\"));
        }

        private static async Task EnsureMcpClientAsync()
        {
            string ecosystemRoot = GetEcosystemRoot();
            string libDir = Path.Combine(ecosystemRoot, "lib");
            SetDllDirectory(libDir);

            if (_mcpClient == null)
            {
                _mcpClient = new McpClient();
            }

            string pythonExe = Path.Combine(ecosystemRoot, @"Python_Runtime\python.exe");
            string serverPy = Path.Combine(ecosystemRoot, @"mcp_server\server.py");

            await _mcpClient.EnsureStartedAsync(pythonExe, serverPy);
        }

        public static async Task<string> RunTriageAsync(string targetFile)
        {
            if (!File.Exists(targetFile))
            {
                return JsonSerializer.Serialize(new { error = "File not found." });
            }

            try
            {
                // 1. Ensure Persistent Python Server is running
                await EnsureMcpClientAsync();

                // 2. Call triage_file via MCP JSON-RPC
                var mcpArgs = new JsonObject { ["path"] = targetFile };
                JsonNode triageNode = await _mcpClient!.CallToolAsync("triage_file", mcpArgs);
                JsonObject rootNode = (triageNode as JsonObject) ?? new JsonObject();

                // 3. Authenticode Check (For Windows/PE)
                string platform = rootNode["platform"]?.ToString() ?? "unknown";
                string fileType = rootNode["file_type"]?.ToString() ?? "unknown";
                if (platform == "windows" || fileType == "pe")
                {
                    string authJson = AuthenticodeVerifier.VerifySignature(targetFile);
                    try
                    {
                        var authNode = JsonNode.Parse(authJson);
                        rootNode["authenticode"] = authNode;
                    }
                    catch
                    {
                        rootNode["authenticode"] = authJson;
                    }
                }
                // 3.5 DIE (Detect It Easy) Scan
                string ecosystemRoot = GetEcosystemRoot();
                string dieDbPath = Path.Combine(ecosystemRoot, @"Rules\die_db");
                string? dieOutput = DieWrapper.Scan(targetFile, dieDbPath);
                if (!string.IsNullOrEmpty(dieOutput))
                {
                    rootNode["die_analysis"] = dieOutput.Trim();
                }

                // 4. Binwalk Scan (If unknown structure or explicitly requested)
                string packer = rootNode["packer"]?.ToString() ?? "unknown";
                if (fileType == "unknown" && packer == "unknown")
                {
                    string? binwalkJson = BinwalkWrapper.Scan(targetFile);
                    if (binwalkJson != null)
                    {
                        try
                        {
                            var binwalkNode = JsonNode.Parse(binwalkJson);
                            
                            if (rootNode["entropy"] == null)
                            {
                                rootNode["entropy"] = new JsonObject();
                            }
                            
                            if (rootNode["entropy"] is JsonObject entropyObj)
                            {
                                entropyObj["deep_scan"] = binwalkNode;
                            }
                        }
                        catch
                        {
                            if (rootNode["entropy"] is JsonObject entropyObj)
                            {
                                entropyObj["deep_scan"] = binwalkJson;
                            }
                        }
                    }
                }

                return rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"Triage failed: {ex.Message}" });
            }
        }

        public static string GetAIFriendlyTriageContext(string rawJson)
        {
            try
            {
                var root = JsonNode.Parse(rawJson) as JsonObject;
                if (root == null) return "{}";

                var aiContext = new JsonObject();
                
                // Keep vital heuristics
                aiContext["file_type"] = root["file_type"]?.ToString();
                aiContext["platform"] = root["platform"]?.ToString();
                aiContext["architecture"] = root["architecture"]?.ToString();
                aiContext["packer"] = root["packer"]?.ToString();
                aiContext["engine"] = root["engine"]?.ToString();
                aiContext["recommended_pipeline"] = root["recommended_pipeline"]?.ToString();
                aiContext["die_analysis"] = root["die_analysis"]?.ToString();
                
                // Process Entropy to only keep suspicious sections
                if (root["entropy"] is JsonObject entropy)
                {
                    var aiEntropy = new JsonObject();
                    aiEntropy["overall"] = entropy["overall"]?.ToString();
                    
                    if (entropy["sections"] is JsonArray sections)
                    {
                        var highEntropySections = new JsonArray();
                        foreach (var sec in sections)
                        {
                            if (sec is JsonObject secObj && secObj["entropy"] != null)
                            {
                                double ent = (double)secObj["entropy"]!;
                                if (ent > 7.0) // Only keep highly packed sections to save tokens
                                {
                                    highEntropySections.Add(new JsonObject
                                    {
                                        ["name"] = secObj["name"]?.ToString(),
                                        ["entropy"] = ent
                                    });
                                }
                            }
                        }
                        if (highEntropySections.Count > 0)
                        {
                            aiEntropy["suspicious_sections"] = highEntropySections;
                        }
                    }
                    aiContext["entropy"] = aiEntropy;
                }

                // Keep only suspicious imports
                if (root["imports_summary"] is JsonObject imports)
                {
                    aiContext["suspicious_imports"] = imports["suspicious"]?.DeepClone();
                }

                // Simplified Authenticode
                if (root["authenticode"] is JsonObject auth)
                {
                    aiContext["authenticode_status"] = auth["Status"]?.ToString();
                }

                return aiContext.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return "{\"error\": \"Could not parse triage data for AI\"}";
            }
        }
    }
}


