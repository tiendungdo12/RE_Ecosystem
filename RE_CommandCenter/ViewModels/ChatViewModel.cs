using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RE_CommandCenter.Core;
using RE_CommandCenter.Core.Mcp;
using RE_CommandCenter.Core.Routing;

namespace RE_CommandCenter.ViewModels
{
    public partial class ChatMessage : ObservableObject
    {
        [ObservableProperty]
        public partial string Role { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string Content { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ThinkingVisibility))]
        public partial bool IsThinking { get; set; }

        public Microsoft.UI.Xaml.Visibility ThinkingVisibility => IsThinking ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public partial class ChatViewModel : ObservableObject
    {
        private readonly OllamaClient _ollamaClient;
        private string _systemPrompt = string.Empty;

        public ObservableCollection<ChatMessage> ChatHistory { get; } = new ObservableCollection<ChatMessage>();

        [ObservableProperty]
        public partial string UserInput { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        public ChatViewModel()
        {
            _ollamaClient = new OllamaClient();
        }

        // Nap cau hinh agent theo kieu config-first cua Giai doan 1.
        // IDENTITY.md dinh nghia vai tro, ROUTING_RULES.yaml dinh nghia huong dieu phoi.
        public async Task InitializeAsync()
        {
            string identity = await AgentConfigLoader.LoadIdentityAsync();
            string rules = await AgentConfigLoader.LoadRoutingRulesAsync();
            _systemPrompt = $"{identity}\n\nRules:\n{rules}";
        }

        [RelayCommand]
        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(UserInput) || IsBusy)
            {
                return;
            }

            await InitializeAsync();

            // Dua message cua user vao ChatHistory truoc de UI hien thi ngay.
            var userMsg = new ChatMessage { Role = "User", Content = UserInput };
            ChatHistory.Add(userMsg);

            string prompt = UserInput;
            UserInput = string.Empty;
            IsBusy = true;

            // Tao message placeholder cho AI/tool.
            // Cac chunk streaming ve sau se cap nhat truc tiep Content cua object nay.
            var assistantMsg = new ChatMessage { Role = "AI", Content = "Dang xu ly...", IsThinking = true };
            ChatHistory.Add(assistantMsg);
            
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            try
            {
                // Command tam thoi cho Block 2.3:
                // /md5 bo qua Ollama va goi thang MCP server qua stdio.
                if (prompt.StartsWith("/md5 ", StringComparison.OrdinalIgnoreCase))
                {
                    assistantMsg.Role = "Tool";
                    assistantMsg.Content = await RunMd5ToolAsync(prompt.Substring(5).Trim());
                    assistantMsg.IsThinking = false;
                    return;
                }

                // Command tam thoi cho Block 2.4:
                // /route di qua Branch Table truoc, sau do moi goi MCP tool duoc route chi dinh.
                if (prompt.StartsWith("/route ", StringComparison.OrdinalIgnoreCase))
                {
                    assistantMsg.Role = "Router";
                    assistantMsg.Content = await RunRouteAsync(prompt.Substring(7).Trim());
                    assistantMsg.IsThinking = false;
                    return;
                }

                // Command tam thoi cho Block 3.1:
                // /triage goi MCP_Static_Analysis/triage_server va tra ve JSON triage co cau truc.
                if (prompt.StartsWith("/triage ", StringComparison.OrdinalIgnoreCase))
                {
                    assistantMsg.Role = "Triage";
                    assistantMsg.Content = "Đang khởi tạo tiến trình Triage...\n";
                    
                    string finalResult = await RunTriageToolAsync(
                        prompt.Substring(8).Trim(),
                        (status) => 
                        {
                            if (dispatcherQueue != null)
                            {
                                dispatcherQueue.TryEnqueue(() => 
                                {
                                    assistantMsg.Content += $"⏳ {status}\n";
                                });
                            }
                            else 
                            {
                                assistantMsg.Content += $"⏳ {status}\n";
                            }
                        }
                    );
                    
                    assistantMsg.Content = finalResult;
                    assistantMsg.IsThinking = false;
                    return;
                }

                bool isFirstChunk = true;
                // Luong chat Giai doan 1:
                // prompt di vao Ollama local, tung chunk text duoc append vao assistantMsg.
                await foreach (string chunk in _ollamaClient.GenerateStreamAsync(prompt, _systemPrompt))
                {
                    if (isFirstChunk)
                    {
                        assistantMsg.Content = chunk;
                        assistantMsg.IsThinking = false;
                        isFirstChunk = false;
                    }
                    else
                    {
                        assistantMsg.Content += chunk;
                    }
                }

                if (isFirstChunk)
                {
                    assistantMsg.IsThinking = false;
                }
            }
            catch (Exception ex)
            {
                assistantMsg.IsThinking = false;
                assistantMsg.Content += $"\n[Error: {ex.Message}]";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Hop den MD5:
        // Input: duong dan file tu chat command.
        // Output: text gon de hien thi lai trong khung chat.
        // Ben trong ham nay la luong MCP that: load registry -> spawn server -> tools/list -> tools/call.
        private static async Task<string> RunMd5ToolAsync(string path)
        {
            path = path.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
            {
                return "Usage: /md5 D:\\path\\to\\file";
            }

            McpToolRegistry registry = await McpToolRegistry.LoadAsync();
            McpToolConfig toolConfig = registry.GetRequiredTool("static.md5");

            await using var client = new McpProcessClient(toolConfig, registry.ProtocolVersion);
            await client.InitializeAsync();

            JsonElement tools = await client.ListToolsAsync();
            bool hasMd5Tool = tools
                .GetProperty("tools")
                .EnumerateArray()
                .Any(tool => tool.TryGetProperty("name", out JsonElement name) && name.GetString() == "md5_file");

            if (!hasMd5Tool)
            {
                return "MCP server started, but md5_file was not found in tools/list.";
            }

            JsonElement result = await client.CallToolAsync("md5_file", new { path });
            JsonElement structured = result.TryGetProperty("structuredContent", out JsonElement structuredContent)
                ? structuredContent
                : result;

            if (structured.TryGetProperty("path", out JsonElement resultPath) &&
                structured.TryGetProperty("md5", out JsonElement md5) &&
                structured.TryGetProperty("size", out JsonElement size))
            {
                return $"MD5: {md5.GetString()}\nSize: {size.GetInt64()} bytes\nPath: {resultPath.GetString()}";
            }

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        // Hop den Static Triage:
        // Input: duong dan file.
        // Output: JSON triage gom hash, magic, file_type, platform, entropy, signals.
        // Day la output ma Branch Table se dung lam facts trong cac block tiep theo.
        private static async Task<string> RunTriageToolAsync(string path, Action<string>? onProgress = null)
        {
            path = path.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
            {
                return "Usage: /triage D:\\path\\to\\file";
            }

            JsonElement triage = await RunTriageAsync(path, onProgress);
            
            // Neu server tra ve markdown report, hien thi no tren UI luon thay vi raw JSON
            if (triage.TryGetProperty("markdown_report", out JsonElement mdElement))
            {
                string mdText = mdElement.GetString() ?? "";
                try
                {
                    string appRoot = AgentConfigLoader.GetAppRootPath();
                    string reportsDir = System.IO.Path.Combine(appRoot, "Reports");
                    System.IO.Directory.CreateDirectory(reportsDir);

                    string fileName = System.IO.Path.GetFileName(path);
                    string md5 = "unknown";
                    if (triage.TryGetProperty("hashes", out JsonElement hashes) && 
                        hashes.TryGetProperty("md5", out JsonElement md5Element))
                    {
                        md5 = md5Element.GetString() ?? "unknown";
                    }

                    string outPath = System.IO.Path.Combine(reportsDir, $"{fileName}_{md5}.md");
                    
                    // Triage luon la buoc dau tien, nen ta se viet moi/ghi de de reset lich su file.
                    string header = $"# 🛡️ Hồ Sơ Phân Tích Toàn Diện: {fileName}\n> Khởi tạo: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n";
                    System.IO.File.WriteAllText(outPath, header + mdText, System.Text.Encoding.UTF8);
                    
                    mdText += $"\n\n---\n📁 *Hồ sơ phân tích đã được tạo tại:* `{outPath}`";
                }
                catch (Exception ex)
                {
                    mdText += $"\n\n[Warning: Cannot save report file: {ex.Message}]";
                }
                return mdText;
            }

            return JsonSerializer.Serialize(triage, new JsonSerializerOptions { WriteIndented = true });
        }

        // Hop den Router:
        // Input: duong dan file tu chat command.
        // Output: triage facts + route duoc chon + ket qua tool MCP neu route da enabled.
        // Tu Block 3.2, router khong doan theo extension nua ma dua vao JSON cua static.triage.
        private static async Task<string> RunRouteAsync(string path)
        {
            path = path.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
            {
                return "Usage: /route D:\\path\\to\\file";
            }

            JsonElement triage = await RunTriageAsync(path);
            RouteInput input = RouteInput.FromTriage(triage);
            BranchTableRouter router = await BranchTableRouter.LoadAsync();
            RouteSelection selection = router.Select(input);

            if (!selection.Matched || selection.Rule == null)
            {
                return
                    $"No route matched.\n" +
                    $"FileType: {input.FileType}\n" +
                    $"Platform: {input.Platform}\n" +
                    $"Engine: {input.Engine}\n" +
                    $"RecommendedPipeline: {input.RecommendedPipeline}\n" +
                    $"Path: {input.Path}\n\n" +
                    "Triage:\n" +
                    JsonSerializer.Serialize(triage, new JsonSerializerOptions { WriteIndented = true });
            }

            RouteRule rule = selection.Rule;

            if (!rule.Enabled || rule.Planned)
            {
                return
                    $"Route: {rule.Id}\n" +
                    $"Status: planned\n" +
                    $"Pipeline: {rule.Action.Pipeline}\n" +
                    $"PlannedTool: {rule.Action.ToolId}.{rule.Action.ToolName}\n" +
                    $"FileType: {input.FileType}\n" +
                    $"Platform: {input.Platform}\n" +
                    $"Engine: {input.Engine}\n" +
                    $"Packer: {input.Packer}\n" +
                    $"RecommendedPipeline: {input.RecommendedPipeline}\n" +
                    $"Input: {input.Path}\n\n" +
                    "Triage:\n" +
                    JsonSerializer.Serialize(triage, new JsonSerializerOptions { WriteIndented = true });
            }

            JsonElement result = await CallConfiguredToolAsync(rule.Action.ToolId, rule.Action.ToolName, input.Path);
            string formattedResult = FormatToolResult(result);

            return
                $"Route: {rule.Id}\n" +
                $"Pipeline: {rule.Action.Pipeline}\n" +
                $"Tool: {rule.Action.ToolId}.{rule.Action.ToolName}\n" +
                $"FileType: {input.FileType}\n" +
                $"Platform: {input.Platform}\n" +
                $"RecommendedPipeline: {input.RecommendedPipeline}\n" +
                $"Input: {input.Path}\n\n" +
                formattedResult;
        }

        // Goi MCP tool theo id trong MCP_TOOLS.yaml.
        // Router khong can biet tool nam o dau hay chay bang runtime nao.
        private static async Task<JsonElement> CallConfiguredToolAsync(string toolId, string toolName, string path, Action<string>? onProgress = null)
        {
            McpToolRegistry registry = await McpToolRegistry.LoadAsync();
            McpToolConfig toolConfig = registry.GetRequiredTool(toolId);

            await using var client = new McpProcessClient(toolConfig, registry.ProtocolVersion);
            await client.InitializeAsync();

            return await client.CallToolAsync(toolName, new { path }, onProgress);
        }

        private static string FormatToolResult(JsonElement result)
        {
            JsonElement structured = result.TryGetProperty("structuredContent", out JsonElement structuredContent)
                ? structuredContent
                : result;

            if (structured.TryGetProperty("path", out JsonElement resultPath) &&
                structured.TryGetProperty("md5", out JsonElement md5) &&
                structured.TryGetProperty("size", out JsonElement size))
            {
                return $"MD5: {md5.GetString()}\nSize: {size.GetInt64()} bytes\nPath: {resultPath.GetString()}";
            }

            return JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true });
        }

        private static async Task<JsonElement> RunTriageAsync(string path, Action<string>? onProgress = null)
        {
            JsonElement result = await CallConfiguredToolAsync("static.triage", "triage_file", path, onProgress);
            return result.TryGetProperty("structuredContent", out JsonElement structuredContent)
                ? structuredContent.Clone()
                : result.Clone();
        }
    }
}
