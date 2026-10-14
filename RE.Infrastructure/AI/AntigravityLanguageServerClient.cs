using System;
using RE.Core;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RE.Infrastructure.AI;

public sealed record AntigravityAccountInfo(
    bool IsAuthenticated,
    string? DisplayName,
    string? Email,
    string? PlanName);

public sealed record AntigravityUsageBucket(
    string DisplayName,
    string Window,
    double RemainingPercent,
    double UsedPercent,
    DateTimeOffset? ResetTime);

public sealed record AntigravityUsageGroup(
    string DisplayName,
    IReadOnlyList<AntigravityUsageBucket> Buckets);

public sealed record AntigravityUsageSnapshot(
    string GroupName,
    double SessionUsedPercent,
    double WeeklyUsedPercent,
    DateTimeOffset? SessionResetTime,
    DateTimeOffset? WeeklyResetTime,
    IReadOnlyList<AntigravityUsageGroup> Groups);

public sealed record AntigravityModelInfo(
    string Id,
    string DisplayName,
    string ModelProvider,
    bool SupportsThinking,
    bool Recommended);

public sealed record AntigravityCascadeModel(
    string Label,
    string ModelId,
    string ModelEnum,
    string? TagTitle,
    bool SupportsImages,
    bool IsRecommended,
    double? RemainingFraction,
    DateTimeOffset? ResetTime);

public sealed record AntigravityCascadeModelsResult(
    IReadOnlyList<AntigravityCascadeModel> Models,
    IReadOnlyList<string> RecommendedLabels,
    string? DefaultModelEnum);

public sealed record AntigravityAvailableModels(
    IReadOnlyList<AntigravityModelInfo> Models,
    IReadOnlyList<string> RecommendedModelOrder,
    string DefaultModelId,
    IReadOnlyDictionary<string, string> DeprecatedModelMappings);

public sealed class AntigravityLanguageServerClient
{
    private const string ServiceRoute = "exa.language_server_pb.LanguageServerService";
    private readonly HttpClient _httpClient;

    public AntigravityLanguageServerClient()
    {
        var handler = new HttpClientHandler
        {
            // The local HTTPS listener uses a language-server certificate. The
            // normal path is HTTP/Connect; this is only a fallback for older
            // Antigravity builds that do not expose the HTTP listener.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<AntigravityAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerAsync(cancellationToken);
        using var auth = await PostJsonAsync(server, "HasAuthToken", new { }, cancellationToken);
        bool authenticated = GetBoolean(auth.RootElement, "hasToken");
        if (!authenticated)
            return new AntigravityAccountInfo(false, null, null, null);

        try
        {
            using var status = await PostJsonAsync(server, "GetUserStatus", new { }, cancellationToken);
            var userStatus = GetObject(status.RootElement, "userStatus");
            var planInfo = GetObject(GetObject(userStatus, "planStatus"), "planInfo");
            return new AntigravityAccountInfo(
                true,
                GetString(userStatus, "name"),
                GetString(userStatus, "email"),
                GetString(planInfo, "planName"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Antigravity user status unavailable: {ex.Message}");
            return new AntigravityAccountInfo(true, null, null, null);
        }
    }

    public async Task<bool> HasAuthTokenAsync(CancellationToken cancellationToken = default)
    {
        var info = await GetAccountInfoAsync(cancellationToken);
        return info.IsAuthenticated;
    }

    public async Task<AntigravityUsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerAsync(cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var requestToken = cts.Token;

        JsonDocument? document = null;
        try
        {
            document = await PostJsonAsync(server, "RetrieveUserQuotaSummary", new { }, requestToken);
        }
        catch
        {
            // Fallback to GetUserStatus if RetrieveUserQuotaSummary is unsupported
            try
            {
                document = await PostJsonAsync(server, "GetUserStatus", new { }, requestToken);
            }
            catch
            {
            }
        }

        if (document == null)
            return new AntigravityUsageSnapshot("", 0, 0, null, null, Array.Empty<AntigravityUsageGroup>());

        using (document)
        {
            var root = document.RootElement;
            JsonElement groups = default;

            // 1. Try response.groups
            if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object &&
                response.TryGetProperty("groups", out var g1) && g1.ValueKind == JsonValueKind.Array)
            {
                groups = g1;
            }
            // 2. Try root.groups
            else if (root.TryGetProperty("groups", out var g2) && g2.ValueKind == JsonValueKind.Array)
            {
                groups = g2;
            }
            // 3. Try userStatus.quotaSummary.groups
            else if (root.TryGetProperty("userStatus", out var userStatus) &&
                     userStatus.TryGetProperty("quotaSummary", out var qs) &&
                     qs.TryGetProperty("groups", out var g3) && g3.ValueKind == JsonValueKind.Array)
            {
                groups = g3;
            }
            // 4. Try quotaSummary.groups
            else if (root.TryGetProperty("quotaSummary", out var qsDirect) &&
                     qsDirect.TryGetProperty("groups", out var g4) && g4.ValueKind == JsonValueKind.Array)
            {
                groups = g4;
            }

            if (groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() == 0)
                return new AntigravityUsageSnapshot("", 0, 0, null, null, Array.Empty<AntigravityUsageGroup>());

            var usageGroups = new List<AntigravityUsageGroup>();
            foreach (var groupElement in groups.EnumerateArray())
            {
                var usageBuckets = new List<AntigravityUsageBucket>();
                if (groupElement.TryGetProperty("buckets", out var groupBuckets) &&
                    groupBuckets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var bucket in groupBuckets.EnumerateArray())
                    {
                        double remainingFraction = GetDouble(bucket, "remainingFraction", 1);
                        usageBuckets.Add(new AntigravityUsageBucket(
                            GetString(bucket, "displayName") ?? "",
                            GetString(bucket, "window") ?? "",
                            Math.Clamp(remainingFraction * 100, 0, 100),
                            ToUsedPercent(remainingFraction),
                            ParseDateTimeOffset(GetString(bucket, "resetTime"))));
                    }
                }

                usageGroups.Add(new AntigravityUsageGroup(
                    GetString(groupElement, "displayName") ?? "",
                    usageBuckets));
            }

            // The first group is the Gemini group for current Antigravity builds;
            // keep all groups because Claude/GPT has a separate quota pool.
            var group = groups[0];
            double sessionRemaining = 1;
            double weeklyRemaining = 1;
            DateTimeOffset? sessionReset = null;
            DateTimeOffset? weeklyReset = null;

            if (group.TryGetProperty("buckets", out var buckets) && buckets.ValueKind == JsonValueKind.Array)
            {
                foreach (var bucket in buckets.EnumerateArray())
                {
                    string window = GetString(bucket, "window") ?? "";
                    double remaining = GetDouble(bucket, "remainingFraction", 1);
                    var reset = ParseDateTimeOffset(GetString(bucket, "resetTime"));

                    if (window.Equals("5h", StringComparison.OrdinalIgnoreCase))
                    {
                        sessionRemaining = remaining;
                        sessionReset = reset;
                    }
                    else if (window.Equals("weekly", StringComparison.OrdinalIgnoreCase))
                    {
                        weeklyRemaining = remaining;
                        weeklyReset = reset;
                    }
                }
            }

            return new AntigravityUsageSnapshot(
                GetString(group, "displayName") ?? "",
                ToUsedPercent(sessionRemaining),
                ToUsedPercent(weeklyRemaining),
                sessionReset,
                weeklyReset,
                usageGroups);
        }
    }

    public async Task<AntigravityAvailableModels> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerAsync(cancellationToken);
        using var document = await PostJsonAsync(server, "GetAvailableModels", new { }, cancellationToken);
        var response = GetObject(document.RootElement, "response");

        var models = new List<AntigravityModelInfo>();
        var recommendedOrder = new List<string>();
        string defaultModelId = GetString(response, "defaultAgentModelId") ?? "gemini-3.7-flash-high";
        var deprecatedMappings = new Dictionary<string, string>();

        // Parse agentModelSorts to get recommended order
        if (response.TryGetProperty("agentModelSorts", out var sorts) && sorts.ValueKind == JsonValueKind.Array)
        {
            foreach (var sort in sorts.EnumerateArray())
            {
                if (sort.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
                {
                    foreach (var group in groups.EnumerateArray())
                    {
                        if (group.TryGetProperty("modelIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var id in ids.EnumerateArray())
                            {
                                string? modelId = id.GetString();
                                if (!string.IsNullOrEmpty(modelId))
                                    recommendedOrder.Add(modelId);
                            }
                        }
                    }
                }
            }
        }

        // Parse model definitions
        if (response.TryGetProperty("models", out var modelsObj) && modelsObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in modelsObj.EnumerateObject())
            {
                string modelId = prop.Name;
                var modelDef = prop.Value;
                bool isRecommended = recommendedOrder.Contains(modelId);
                models.Add(new AntigravityModelInfo(
                    modelId,
                    GetString(modelDef, "displayName") ?? modelId,
                    GetString(modelDef, "modelProvider") ?? "",
                    GetBoolean(modelDef, "supportsThinking"),
                    isRecommended || GetBoolean(modelDef, "recommended")));
            }
        }

        // Parse deprecated model mappings
        if (response.TryGetProperty("deprecatedModelIds", out var deprecated) &&
            deprecated.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in deprecated.EnumerateObject())
            {
                string? newId = GetString(prop.Value, "newModelId");
                if (!string.IsNullOrEmpty(newId))
                    deprecatedMappings[prop.Name] = newId;
            }
        }

        return new AntigravityAvailableModels(models, recommendedOrder, defaultModelId, deprecatedMappings);
    }

    public async Task<AntigravityCascadeModelsResult> GetCascadeModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerAsync(cancellationToken);
        JsonDocument? document = null;
        try
        {
            document = await PostJsonAsync(server, "GetCascadeModelConfigData", new { }, cancellationToken);
        }
        catch
        {
            // Fallback to GetUserStatus if GetCascadeModelConfigData is not available
            document = await PostJsonAsync(server, "GetUserStatus", new { }, cancellationToken);
        }

        using (document)
        {
            var root = document.RootElement;
            JsonElement configData = root;

            // In GetUserStatus, cascadeModelConfigData is under userStatus.cascadeModelConfigData
            if (root.TryGetProperty("userStatus", out var userStatus) &&
                userStatus.TryGetProperty("cascadeModelConfigData", out var cascadeData))
            {
                configData = cascadeData;
            }
            else if (root.TryGetProperty("response", out var resp))
            {
                configData = resp;
            }

            var models = new List<AntigravityCascadeModel>();
            var recommendedLabels = new List<string>();
            string? defaultModelEnum = null;

            if (configData.TryGetProperty("defaultOverrideModelConfig", out var defaultOverride) &&
                defaultOverride.TryGetProperty("modelOrAlias", out var defaultAlias))
            {
                defaultModelEnum = GetString(defaultAlias, "model");
            }

            // Parse sort order
            if (configData.TryGetProperty("clientModelSorts", out var sorts) && sorts.ValueKind == JsonValueKind.Array)
            {
                foreach (var sort in sorts.EnumerateArray())
                {
                    if (sort.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var group in groups.EnumerateArray())
                        {
                            if (group.TryGetProperty("modelLabels", out var labels) &&
                                labels.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var label in labels.EnumerateArray())
                                {
                                    string? l = label.GetString();
                                    if (!string.IsNullOrEmpty(l))
                                        recommendedLabels.Add(l);
                                }
                            }
                        }
                    }
                }
            }

            // Parse clientModelConfigs
            if (configData.TryGetProperty("clientModelConfigs", out var configs) &&
                configs.ValueKind == JsonValueKind.Array)
            {
                foreach (var config in configs.EnumerateArray())
                {
                    string label = GetString(config, "label") ?? "";
                    string modelId = GetString(config, "modelId") ?? "";
                    string modelEnum = "";
                    if (config.TryGetProperty("modelOrAlias", out var alias))
                    {
                        modelEnum = GetString(alias, "model") ?? "";
                    }

                    if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(modelId))
                        continue;

                    string? tagTitle = GetString(config, "tagTitle");
                    bool supportsImages = GetBoolean(config, "supportsImages");
                    bool isRecommended = GetBoolean(config, "isRecommended");

                    double? remainingFraction = null;
                    DateTimeOffset? resetTime = null;
                    if (config.TryGetProperty("quotaInfo", out var quotaInfo) &&
                        quotaInfo.ValueKind == JsonValueKind.Object)
                    {
                        if (quotaInfo.TryGetProperty("remainingFraction", out var rf) &&
                            rf.TryGetDouble(out var fraction))
                            remainingFraction = fraction;
                        resetTime = ParseDateTimeOffset(GetString(quotaInfo, "resetTime"));
                    }

                    models.Add(new AntigravityCascadeModel(
                        label,
                        modelId,
                        modelEnum,
                        tagTitle,
                        supportsImages,
                        isRecommended,
                        remainingFraction,
                        resetTime));
                }
            }

            return new AntigravityCascadeModelsResult(models, recommendedLabels, defaultModelEnum);
        }
    }

    public static async Task SetModelStateAsync(string modelEnum, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelEnum))
            return;

        try
        {
            string profileDir = RuntimeDataPaths.AntigravityProfilePath;
            string appDir = Path.Combine(profileDir, "app");
            Directory.CreateDirectory(appDir);
            string stateFilePath = Path.Combine(appDir, "antigravity_state.pbtxt");

            string lineToWrite = $"last_selected_agent_model: {modelEnum}";
            if (File.Exists(stateFilePath))
            {
                string content = await File.ReadAllTextAsync(stateFilePath, cancellationToken);
                string updated;
                if (System.Text.RegularExpressions.Regex.IsMatch(content, @"(?m)^last_selected_agent_model:\s*.*$"))
                {
                    updated = System.Text.RegularExpressions.Regex.Replace(content,
                        @"(?m)^last_selected_agent_model:\s*.*$", lineToWrite);
                }
                else
                {
                    updated = content.TrimEnd() + Environment.NewLine + lineToWrite + Environment.NewLine;
                }

                await File.WriteAllTextAsync(stateFilePath, updated, cancellationToken);
            }
            else
            {
                await File.WriteAllTextAsync(stateFilePath, lineToWrite + Environment.NewLine, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update antigravity_state.pbtxt: {ex.Message}");
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerAsync(cancellationToken);
        using var response = await PostJsonAsync(server, "AuthLogout", new { }, cancellationToken);
        // Restarting the local server ensures a subsequent sign-in gets a new
        // OAuth challenge instead of reusing the authenticated process state.
        server.Stop();
    }

    /// <summary>
    /// Starts the language server's browser-login flow. The RPC keeps
    /// its HTTP response open until OAuth completes, so it is intentionally
    /// launched in the background. The server owns the OAuth state/PKCE pair
    /// and opens the user's default browser itself.
    /// </summary>
    /// <summary>
    /// Starts authentication and returns the long-running RPC task. Callers
    /// can observe that task to stop their UI promptly when the server reports
    /// an OAuth failure instead of waiting for an arbitrary timeout.
    /// </summary>
    public async Task<Task> BeginBrowserLoginAsync(CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerAsync(cancellationToken);
        return BeginBrowserLoginCoreAsync(server);
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        string prompt,
        string systemPrompt,
        string model,
        string workspace,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        AntigravityServerManager? server = null;
        string? startupError = null;
        try
        {
            server = await EnsureServerAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            startupError = ex.Message;
        }

        if (server == null)
        {
            yield return $"Error: Không thể khởi động Antigravity language server: {startupError}";
            yield break;
        }

        if (!await HasAuthTokenAsync(cancellationToken))
        {
            yield return "Error: Antigravity chưa đăng nhập Google. Hãy mở mục Cài đặt và đăng nhập trước.";
            yield break;
        }

        string effectivePrompt = BuildPrompt(prompt, systemPrompt);
        string conversationId = "";
        string? conversationError = null;
        try
        {
            conversationId = await StartConversationViaAgentApiAsync(
                server,
                NormalizeAgentApiModel(model),
                effectivePrompt,
                workspace,
                cancellationToken);
        }
        catch (Exception ex)
        {
            conversationError = ex.Message;
        }

        if (conversationError != null || string.IsNullOrWhiteSpace(conversationId))
        {
            yield return
                $"Error: Không thể tạo cuộc hội thoại Antigravity: {conversationError ?? "conversationId trống"}";
            yield break;
        }

        string? lastResponse = null;
        bool yieldedAnyResponse = false;

        await foreach (var update in StreamAgentStateUpdatesAsync(server, conversationId, cancellationToken))
        {
            if (update.ErrorMessage != null)
            {
                yield return $"Error: {update.ErrorMessage}";
                yield break;
            }

            if (update.ResponseText != null)
            {
                string next = update.ResponseText;
                if (lastResponse == null)
                {
                    lastResponse = next;
                    yieldedAnyResponse = true;
                    yield return next;
                }
                else if (next.StartsWith(lastResponse, StringComparison.Ordinal))
                {
                    string delta = next[lastResponse.Length..];
                    lastResponse = next;
                    if (delta.Length > 0)
                    {
                        yieldedAnyResponse = true;
                        yield return delta;
                    }
                }
                else if (!next.Equals(lastResponse, StringComparison.Ordinal))
                {
                    lastResponse = next;
                    yieldedAnyResponse = true;
                    yield return $"\n{next}";
                }
            }

            if (update.IsFullyIdle)
                break;
        }

        if (!yieldedAnyResponse)
            yield return "Error: Antigravity đã kết thúc nhưng không trả về nội dung phản hồi.";
    }

    private async Task<string> StartConversationViaAgentApiAsync(
        AntigravityServerManager server,
        string agentApiModel,
        string prompt,
        string workspace,
        CancellationToken cancellationToken)
    {
        using var result = await RunAgentApiCommandAsync(
            server,
            new[]
            {
                "new-conversation",
                $"--model={agentApiModel}",
                "--title=RE Ecosystem",
                prompt
            },
            workspace,
            cancellationToken);

        var response = GetObject(result.RootElement, "response");
        var conversation = GetObject(response, "newConversation");
        string? conversationId = GetString(conversation, "conversationId");
        if (string.IsNullOrWhiteSpace(conversationId))
            throw new InvalidOperationException("Agent API không trả về conversationId.");

        return conversationId;
    }

    private async IAsyncEnumerable<AgentUpdate> StreamAgentStateUpdatesAsync(
        AntigravityServerManager server,
        string conversationId,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        string baseUrl = GetBaseUrl(server);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/{ServiceRoute}/StreamAgentStateUpdates");
        request.Headers.TryAddWithoutValidation("X-Codeium-Csrf-Token", server.CsrfToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/connect+json"));

        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { conversationId }));
        request.Content = new ByteArrayContent(FrameConnectMessage(payload));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/connect+json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(cancellationToken);
            yield return new AgentUpdate(null, false, $"Kết nối stream thất bại ({(int)response.StatusCode}): {error}");
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            ConnectFrame? frame = await ReadConnectFrameAsync(stream, cancellationToken);
            if (frame == null)
                yield break;

            // Flag 1 is a trailer/error frame. The JSON body is still useful
            // for diagnostics, but it is not a trajectory update.
            using var document = JsonDocument.Parse(frame.Payload);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var errorElement))
            {
                yield return new AgentUpdate(null, false, GetErrorText(errorElement));
                yield break;
            }

            if (!root.TryGetProperty("update", out var update))
                continue;

            string? responseText = null;
            string? errorMessage = null;
            if (update.TryGetProperty("mainTrajectoryUpdate", out var trajectory) &&
                trajectory.TryGetProperty("stepsUpdate", out var stepsUpdate) &&
                stepsUpdate.TryGetProperty("steps", out var steps) &&
                steps.ValueKind == JsonValueKind.Array)
            {
                foreach (var step in steps.EnumerateArray())
                {
                    string? type = GetString(step, "type");
                    if (type == "CORTEX_STEP_TYPE_PLANNER_RESPONSE" &&
                        step.TryGetProperty("plannerResponse", out var plannerResponse))
                    {
                        responseText = GetString(plannerResponse, "modifiedResponse") ?? responseText;
                    }

                    if (type == "CORTEX_STEP_TYPE_ERROR_MESSAGE" &&
                        step.TryGetProperty("errorMessage", out var stepError))
                    {
                        errorMessage = GetErrorText(stepError);
                    }
                }
            }

            yield return new AgentUpdate(
                responseText,
                GetBoolean(update, "fullyIdle"),
                errorMessage);
        }
    }

    private async Task<JsonDocument> PostJsonAsync(
        AntigravityServerManager server,
        string method,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{GetBaseUrl(server)}/{ServiceRoute}/{method}");
        request.Headers.TryAddWithoutValidation("X-Codeium-Csrf-Token", server.CsrfToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{method} trả về {(int)response.StatusCode}: {text}");

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private async Task BeginBrowserLoginCoreAsync(AntigravityServerManager server)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            // LoginWithBrowser was removed in language server 2.6.0.
            // Login is the current RPC and remains open until OAuth
            // completes or the caller disconnects.
            $"{GetBaseUrl(server)}/{ServiceRoute}/Login");
        request.Headers.TryAddWithoutValidation("X-Codeium-Csrf-Token", server.CsrfToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        // This request normally completes only after the browser callback.
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Đăng nhập Antigravity thất bại ({(int)response.StatusCode}): {error}");
        }
    }

    private async Task<JsonDocument> RunAgentApiCommandAsync(
        AntigravityServerManager server,
        IReadOnlyList<string> arguments,
        string workspace,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = server.LanguageServerPath,
            WorkingDirectory = RuntimeDataPaths.WorkspacePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("agentapi");
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.Environment["ANTIGRAVITY_LS_ADDRESS"] = GetBaseUrl(server);
        startInfo.Environment["ANTIGRAVITY_CSRF_TOKEN"] = server.CsrfToken;
        startInfo.Environment["ANTIGRAVITY_PROJECT_ID"] =
            Directory.Exists(workspace) ? Path.GetFullPath(workspace) : RuntimeDataPaths.WorkspacePath;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Không thể khởi động language_server.exe agentapi.");

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim());

        if (string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException("Agent API không trả về JSON.");

        var document = JsonDocument.Parse(stdout);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            string message = GetErrorText(error);
            document.Dispose();
            throw new InvalidOperationException(message);
        }

        return document;
    }

    private async Task<AntigravityServerManager> EnsureServerAsync(CancellationToken cancellationToken)
    {
        var server = AntigravityServerManager.Instance;
        if (!server.IsRunning && !await server.StartAsync())
            throw new InvalidOperationException("language_server.exe không khởi động được.");

        cancellationToken.ThrowIfCancellationRequested();
        return server;
    }

    private static string GetBaseUrl(AntigravityServerManager server)
    {
        return server.HttpPort > 0 ? server.HttpBaseAddress : server.HttpsBaseAddress;
    }

    private static string BuildPrompt(string prompt, string systemPrompt)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
            return prompt;

        return
            $"System instructions (follow these instructions while answering):\n{systemPrompt}\n\nUser request:\n{prompt}";
    }

    private static string NormalizeAgentApiModel(string model)
    {
        string value = (model ?? "").Trim();
        string lower = value.ToLowerInvariant();
        if (lower.Contains("lite", StringComparison.Ordinal))
            return "flash_lite";
        if (lower.Contains("pro", StringComparison.Ordinal) ||
            lower.Contains("claude", StringComparison.Ordinal) ||
            lower.Contains("opus", StringComparison.Ordinal) ||
            lower.Contains("gpt", StringComparison.Ordinal) ||
            lower.Contains("thinking", StringComparison.Ordinal))
            return "pro";
        return "flash";
    }

    private static byte[] FrameConnectMessage(byte[] payload)
    {
        byte[] frame = new byte[payload.Length + 5];
        frame[0] = 0;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1, 4), payload.Length);
        Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);
        return frame;
    }

    private static async Task<ConnectFrame?> ReadConnectFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[5];
        if (!await ReadExactlyAsync(stream, header, cancellationToken))
            return null;

        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1, 4));
        if (length < 0 || length > 64 * 1024 * 1024)
            throw new InvalidDataException($"Connect frame không hợp lệ: {length} bytes.");

        byte[] payload = new byte[length];
        if (!await ReadExactlyAsync(stream, payload, cancellationToken))
            return null;

        return new ConnectFrame(header[0], Encoding.UTF8.GetString(payload));
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                return false;
            offset += read;
        }

        return true;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static JsonElement GetObject(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Object
            ? value
            : default;
    }

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    private static double GetDouble(JsonElement element, string propertyName, double fallback)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.TryGetDouble(out var number)
            ? number
            : fallback;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, out var result) ? result : null;
    }

    private static double ToUsedPercent(double remainingFraction)
    {
        return Math.Clamp((1 - remainingFraction) * 100, 0, 100);
    }

    private static string GetErrorText(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.String)
            return error.GetString() ?? "Unknown Antigravity error.";

        return GetString(error, "message") ??
               GetString(error, "shortError") ??
               GetString(GetObject(error, "error"), "shortError") ??
               "Unknown Antigravity error.";
    }

    private sealed record ConnectFrame(byte Flags, string Payload);

    private sealed record AgentUpdate(string? ResponseText, bool IsFullyIdle, string? ErrorMessage);
}