using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RE.Core.Mcp;
using RE.Domain.Models;

namespace RE.Core.Services
{
    public class BurpMcpService
    {
        public const string DefaultToolId = "network.burp";
        private readonly McpClientManager _mcpManager;

        public BurpMcpService(McpClientManager mcpManager)
        {
            _mcpManager = mcpManager;
        }

        // ==========================================
        // 1. DISCOVERY & STATUS
        // ==========================================

        /// <summary>
        /// Kiểm tra trạng thái kết nối tới Burp Suite MCP Gateway qua HTTP/SSE.
        /// </summary>
        public async Task<bool> IsConnectedAsync(string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var status = await _mcpManager.CheckStatusAsync(toolId, ct);
            return status.IsConnected;
        }

        /// <summary>
        /// Lấy chi tiết trạng thái kết nối và số lượng công cụ khả dụng.
        /// </summary>
        public async Task<McpConnectionStatus> GetStatusAsync(string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            return await _mcpManager.CheckStatusAsync(toolId, ct);
        }

        // ==========================================
        // 2. HTTP PROXY HISTORY & TRAFFIC
        // ==========================================

        /// <summary>
        /// Lấy danh sách lịch sử HTTP Request/Response trong tab Proxy của Burp Suite.
        /// </summary>
        public async Task<List<BurpHttpHistoryItem>> GetProxyHttpHistoryAsync(int count = 50, int offset = 0,
            string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "get_proxy_http_history", new { count, offset }, ct);
            return ParseHttpHistoryItems(json);
        }

        /// <summary>
        /// Lọc lịch sử HTTP theo biểu thức chính quy (Regex).
        /// </summary>
        public async Task<List<BurpHttpHistoryItem>> SearchProxyHttpHistoryRegexAsync(string regex, int count = 50,
            int offset = 0, string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "get_proxy_http_history_regex",
                new { regex, count, offset }, ct);
            return ParseHttpHistoryItems(json);
        }

        /// <summary>
        /// Lấy danh sách lịch sử WebSocket frames trong tab Proxy.
        /// </summary>
        public async Task<string> GetProxyWebSocketHistoryAsync(int count = 50, int offset = 0,
            string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "get_proxy_websocket_history", new { count, offset },
                ct);
            return json.ToString();
        }

        // ==========================================
        // 3. REPEATER & ACTIVE REQUESTS
        // ==========================================

        /// <summary>
        /// Gửi một HTTP/1.1 Request trực tiếp và nhận Response.
        /// </summary>
        public async Task<string> SendHttpRequest1Async(string content, string? targetHostname = null,
            int? targetPort = null, bool? usesHttps = null, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            var args = new Dictionary<string, object> { { "content", content } };
            if (!string.IsNullOrEmpty(targetHostname)) args["targetHostname"] = targetHostname;
            if (targetPort.HasValue) args["targetPort"] = targetPort.Value;
            if (usesHttps.HasValue) args["usesHttps"] = usesHttps.Value;

            var json = await _mcpManager.CallToolAsync(toolId, "send_http1_request", args, ct);
            return json.ToString();
        }

        /// <summary>
        /// Gửi một HTTP/2 Request trực tiếp và nhận Response.
        /// </summary>
        public async Task<string> SendHttpRequest2Async(List<string> headers, List<string> pseudoHeaders,
            string requestBody, string? targetHostname = null, int? targetPort = null, bool? usesHttps = null,
            string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var args = new Dictionary<string, object>
            {
                { "headers", headers },
                { "pseudoHeaders", pseudoHeaders },
                { "requestBody", requestBody }
            };
            if (!string.IsNullOrEmpty(targetHostname)) args["targetHostname"] = targetHostname;
            if (targetPort.HasValue) args["targetPort"] = targetPort.Value;
            if (usesHttps.HasValue) args["usesHttps"] = usesHttps.Value;

            var json = await _mcpManager.CallToolAsync(toolId, "send_http2_request", args, ct);
            return json.ToString();
        }

        /// <summary>
        /// Tạo một tab Repeater mới trong Burp Suite với raw request HTTP/1.1.
        /// </summary>
        public async Task<string> CreateRepeaterTabAsync(string content, string? tabName = null,
            string? targetHostname = null, int? targetPort = null, bool? usesHttps = null,
            string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var args = new Dictionary<string, object> { { "content", content } };
            if (!string.IsNullOrEmpty(tabName)) args["tabName"] = tabName;
            if (!string.IsNullOrEmpty(targetHostname)) args["targetHostname"] = targetHostname;
            if (targetPort.HasValue) args["targetPort"] = targetPort.Value;
            if (usesHttps.HasValue) args["usesHttps"] = usesHttps.Value;

            var json = await _mcpManager.CallToolAsync(toolId, "create_repeater_tab", args, ct);
            return json.ToString();
        }

        /// <summary>
        /// Tạo một tab Repeater mới trong Burp Suite với request HTTP/2.
        /// </summary>
        public async Task<string> CreateRepeaterTabHttp2Async(List<string> headers, List<string> pseudoHeaders,
            string requestBody, string? tabName = null, string? targetHostname = null, int? targetPort = null,
            bool? usesHttps = null, string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var args = new Dictionary<string, object>
            {
                { "headers", headers },
                { "pseudoHeaders", pseudoHeaders },
                { "requestBody", requestBody }
            };
            if (!string.IsNullOrEmpty(tabName)) args["tabName"] = tabName;
            if (!string.IsNullOrEmpty(targetHostname)) args["targetHostname"] = targetHostname;
            if (targetPort.HasValue) args["targetPort"] = targetPort.Value;
            if (usesHttps.HasValue) args["usesHttps"] = usesHttps.Value;

            var json = await _mcpManager.CallToolAsync(toolId, "create_repeater_tab_http2", args, ct);
            return json.ToString();
        }

        /// <summary>
        /// Gửi HTTP Request sang tab Intruder để chuẩn bị fuzzing/bruteforce.
        /// </summary>
        public async Task<string> SendToIntruderAsync(string content, string? tabName = null,
            string? targetHostname = null, int? targetPort = null, bool? usesHttps = null,
            string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var args = new Dictionary<string, object> { { "content", content } };
            if (!string.IsNullOrEmpty(tabName)) args["tabName"] = tabName;
            if (!string.IsNullOrEmpty(targetHostname)) args["targetHostname"] = targetHostname;
            if (targetPort.HasValue) args["targetPort"] = targetPort.Value;
            if (usesHttps.HasValue) args["usesHttps"] = usesHttps.Value;

            var json = await _mcpManager.CallToolAsync(toolId, "send_to_intruder", args, ct);
            return json.ToString();
        }

        // ==========================================
        // 4. SCANNER & SECURITY ISSUES
        // ==========================================

        /// <summary>
        /// Lấy danh sách các cảnh báo bảo mật và lỗ hổng do Burp Scanner phát hiện.
        /// </summary>
        public async Task<List<BurpScannerIssue>> GetScannerIssuesAsync(int count = 50, int offset = 0,
            string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "get_scanner_issues", new { count, offset }, ct);
            var list = new List<BurpScannerIssue>();

            if (json.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in json.EnumerateArray())
                {
                    list.Add(ParseScannerIssue(item));
                }
            }
            else if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("issues", out var issuesArray) &&
                     issuesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in issuesArray.EnumerateArray())
                {
                    list.Add(ParseScannerIssue(item));
                }
            }

            return list;
        }

        // ==========================================
        // 5. BURP COLLABORATOR (OUT-OF-BAND)
        // ==========================================

        /// <summary>
        /// Sinh ra một URL Burp Collaborator Payload để kiểm thử OOB / SSRF / DNS Callback.
        /// </summary>
        public async Task<BurpCollaboratorPayload> GenerateCollaboratorPayloadAsync(string? customData = null,
            string toolId = DefaultToolId, CancellationToken ct = default)
        {
            object args = string.IsNullOrEmpty(customData) ? new { } : new { customData };
            var json = await _mcpManager.CallToolAsync(toolId, "generate_collaborator_payload", args, ct);
            var payload = new BurpCollaboratorPayload { RawJson = json.ToString() };

            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("payloadUrl", out var pUrl)) payload.PayloadUrl = pUrl.GetString() ?? "";
                if (json.TryGetProperty("payloadId", out var pId)) payload.PayloadId = pId.GetString() ?? "";
            }

            return payload;
        }

        /// <summary>
        /// Kiểm tra các tương tác Out-of-Band (DNS, HTTP, SMTP) gửi về Collaborator.
        /// </summary>
        public async Task<List<BurpCollaboratorInteraction>> GetCollaboratorInteractionsAsync(string? payloadId = null,
            string toolId = DefaultToolId, CancellationToken ct = default)
        {
            object args = string.IsNullOrEmpty(payloadId) ? new { } : new { payloadId };
            var json = await _mcpManager.CallToolAsync(toolId, "get_collaborator_interactions", args, ct);
            var list = new List<BurpCollaboratorInteraction>();

            if (json.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in json.EnumerateArray())
                {
                    list.Add(ParseCollaboratorInteraction(item));
                }
            }

            return list;
        }

        // ==========================================
        // 6. PROXY CONTROL & EDITOR
        // ==========================================

        /// <summary>
        /// Bật hoặc tắt tính năng Intercept (chặn gói tin) của Burp Proxy.
        /// </summary>
        public async Task<bool> SetProxyInterceptStateAsync(bool intercepting, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            await _mcpManager.CallToolAsync(toolId, "set_proxy_intercept_state", new { intercepting }, ct);
            return true;
        }

        /// <summary>
        /// Tạm dừng hoặc tiếp tục Task Execution Engine của Burp Suite.
        /// </summary>
        public async Task<bool> SetTaskExecutionEngineStateAsync(bool running, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            await _mcpManager.CallToolAsync(toolId, "set_task_execution_engine_state", new { running }, ct);
            return true;
        }

        /// <summary>
        /// Đọc nội dung hiện tại đang hiển thị trong message editor hoạt động của người dùng.
        /// </summary>
        public async Task<string> GetActiveEditorContentsAsync(string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "get_active_editor_contents", new { }, ct);
            return json.ToString();
        }

        /// <summary>
        /// Đặt nội dung cho message editor đang hoạt động.
        /// </summary>
        public async Task<bool> SetActiveEditorContentsAsync(string text, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            await _mcpManager.CallToolAsync(toolId, "set_active_editor_contents", new { text }, ct);
            return true;
        }

        // ==========================================
        // 7. UTILITIES
        // ==========================================

        public async Task<string> UrlEncodeAsync(string content, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "url_encode", new { content }, ct);
            return ExtractStringResult(json);
        }

        public async Task<string> UrlDecodeAsync(string content, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "url_decode", new { content }, ct);
            return ExtractStringResult(json);
        }

        public async Task<string> Base64EncodeAsync(string content, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "base64_encode", new { content }, ct);
            return ExtractStringResult(json);
        }

        public async Task<string> Base64DecodeAsync(string content, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "base64_decode", new { content }, ct);
            return ExtractStringResult(json);
        }

        // ==========================================
        // HELPER PARSERS
        // ==========================================

        private static List<BurpHttpHistoryItem> ParseHttpHistoryItems(JsonElement json)
        {
            var list = new List<BurpHttpHistoryItem>();
            if (json.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in json.EnumerateArray())
                {
                    list.Add(ParseHttpHistoryItem(item));
                }
            }
            else if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("items", out var itemsArray) &&
                     itemsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsArray.EnumerateArray())
                {
                    list.Add(ParseHttpHistoryItem(item));
                }
            }

            return list;
        }

        private static BurpHttpHistoryItem ParseHttpHistoryItem(JsonElement item)
        {
            var dto = new BurpHttpHistoryItem { RawJson = item.ToString() };
            if (item.ValueKind != JsonValueKind.Object) return dto;

            if (item.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out int id)) dto.Id = id;
            if (item.TryGetProperty("url", out var urlProp)) dto.Url = urlProp.GetString() ?? "";
            if (item.TryGetProperty("method", out var methodProp)) dto.Method = methodProp.GetString() ?? "";
            if (item.TryGetProperty("statusCode", out var statusProp) && statusProp.TryGetInt32(out int sc))
                dto.StatusCode = sc;
            if (item.TryGetProperty("host", out var hostProp)) dto.Host = hostProp.GetString() ?? "";
            if (item.TryGetProperty("port", out var portProp) && portProp.TryGetInt32(out int p)) dto.Port = p;
            if (item.TryGetProperty("protocol", out var protoProp)) dto.Protocol = protoProp.GetString() ?? "";
            if (item.TryGetProperty("mimeType", out var mimeProp)) dto.MimeType = mimeProp.GetString() ?? "";
            if (item.TryGetProperty("comment", out var commentProp)) dto.Comment = commentProp.GetString() ?? "";

            return dto;
        }

        private static BurpScannerIssue ParseScannerIssue(JsonElement item)
        {
            var issue = new BurpScannerIssue { RawJson = item.ToString() };
            if (item.ValueKind != JsonValueKind.Object) return issue;

            if (item.TryGetProperty("issueName", out var nameProp)) issue.IssueName = nameProp.GetString() ?? "";
            if (item.TryGetProperty("issueType", out var typeProp)) issue.IssueType = typeProp.GetString() ?? "";
            if (item.TryGetProperty("severity", out var sevProp)) issue.Severity = sevProp.GetString() ?? "";
            if (item.TryGetProperty("confidence", out var confProp)) issue.Confidence = confProp.GetString() ?? "";
            if (item.TryGetProperty("url", out var urlProp)) issue.Url = urlProp.GetString() ?? "";
            if (item.TryGetProperty("path", out var pathProp)) issue.Path = pathProp.GetString() ?? "";
            if (item.TryGetProperty("host", out var hostProp)) issue.Host = hostProp.GetString() ?? "";
            if (item.TryGetProperty("issueDetail", out var detailProp))
                issue.IssueDetail = detailProp.GetString() ?? "";
            if (item.TryGetProperty("issueBackground", out var bgProp))
                issue.IssueBackground = bgProp.GetString() ?? "";
            if (item.TryGetProperty("remediationDetail", out var remProp))
                issue.RemediationDetail = remProp.GetString() ?? "";

            return issue;
        }

        private static BurpCollaboratorInteraction ParseCollaboratorInteraction(JsonElement item)
        {
            var interaction = new BurpCollaboratorInteraction { RawJson = item.ToString() };
            if (item.ValueKind != JsonValueKind.Object) return interaction;

            if (item.TryGetProperty("interactionId", out var idProp))
                interaction.InteractionId = idProp.GetString() ?? "";
            if (item.TryGetProperty("type", out var typeProp)) interaction.Type = typeProp.GetString() ?? "";
            if (item.TryGetProperty("clientIp", out var ipProp)) interaction.ClientIp = ipProp.GetString() ?? "";
            if (item.TryGetProperty("timestamp", out var timeProp)) interaction.Timestamp = timeProp.GetString() ?? "";

            return interaction;
        }

        private static string ExtractStringResult(JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.String) return json.GetString() ?? "";
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("result", out var resProp))
            {
                return resProp.GetString() ?? resProp.ToString();
            }

            return json.ToString();
        }
    }
}