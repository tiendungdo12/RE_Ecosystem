using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RE.Core.Mcp;
using RE.Domain.Models;

namespace RE.Core.Services
{
    public class IdaMcpService
    {
        public const string DefaultToolId = "interactive.ida";
        private readonly McpClientManager _mcpManager;

        public IdaMcpService(McpClientManager mcpManager)
        {
            _mcpManager = mcpManager;
        }

        // ==========================================
        // 1. DISCOVERY (Khám phá - ReadOnly, Nhẹ)
        // ==========================================

        /// <summary>
        /// Kiểm tra trạng thái kết nối tới IDA Pro MCP Gateway.
        /// </summary>
        public async Task<bool> IsConnectedAsync(string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var status = await _mcpManager.CheckStatusAsync(toolId, ct);
            return status.IsConnected;
        }

        /// <summary>
        /// Lấy thông tin metadata của file đang mở trong IDA Pro (input_file, arch, bits, endian, hash).
        /// </summary>
        public async Task<IdaMetadata> GetMetadataAsync(string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "get_metadata", new { }, ct);
            var metadata = new IdaMetadata
            {
                RawJson = json.ToString()
            };

            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("input_file", out var inputFile))
                    metadata.InputFile = inputFile.GetString() ?? "";
                if (json.TryGetProperty("arch", out var arch)) metadata.Architecture = arch.GetString() ?? "";
                if (json.TryGetProperty("bits", out var bits)) metadata.Bits = bits.GetInt32();
                if (json.TryGetProperty("endian", out var endian)) metadata.Endian = endian.GetString() ?? "";
                if (json.TryGetProperty("hash", out var hash)) metadata.Hash = hash.GetString() ?? "";
                if (json.TryGetProperty("filetype", out var ft)) metadata.FileType = ft.GetString() ?? "";
            }

            return metadata;
        }

        /// <summary>
        /// Đọc vị trí con trỏ chuột hiện tại của người dùng trong giao diện IDA Pro GUI.
        /// </summary>
        public async Task<IdaCursorInfo> GetCursorAsync(string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "get_cursor", new { }, ct);
            var cursor = new IdaCursorInfo
            {
                RawJson = json.ToString()
            };

            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("ea", out var ea))
                    cursor.Address = ea.GetString() ?? ea.GetInt64().ToString("X");
                if (json.TryGetProperty("address", out var addr))
                    cursor.Address = addr.GetString() ?? addr.GetInt64().ToString("X");
                if (json.TryGetProperty("func_ea", out var fea))
                    cursor.FunctionAddress = fea.GetString() ?? fea.GetInt64().ToString("X");
                if (json.TryGetProperty("func_name", out var fname)) cursor.FunctionName = fname.GetString() ?? "";
                if (json.TryGetProperty("line", out var line)) cursor.DisassemblyLine = line.GetString() ?? "";
            }

            return cursor;
        }

        /// <summary>
        /// Liệt kê danh sách các hàm trong IDB (hỗ trợ phân trang để tránh tràn token).
        /// </summary>
        public async Task<List<IdaFunctionInfo>> ListFunctionsAsync(int offset = 0, int limit = 100,
            string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "list_functions", new { offset, limit }, ct);
            var list = new List<IdaFunctionInfo>();

            if (json.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in json.EnumerateArray())
                {
                    list.Add(ParseFunctionInfo(item));
                }
            }
            else if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("functions", out var funcsArray) &&
                     funcsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in funcsArray.EnumerateArray())
                {
                    list.Add(ParseFunctionInfo(item));
                }
            }

            return list;
        }

        // ==========================================
        // 2. ANALYSIS (Phân tích - ReadOnly)
        // ==========================================

        /// <summary>
        /// Decompile một hàm theo địa chỉ hoặc tên hàm bằng Hex-Rays.
        /// </summary>
        public async Task<string> DecompileAsync(string addressOrName, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "decompile", new { addr = addressOrName }, ct);
            return ExtractStringResult(json);
        }

        /// <summary>
        /// Lấy mã Disassembly tại địa chỉ chỉ định.
        /// </summary>
        public async Task<string> DisasmAsync(string addressOrName, int count = 20, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "disasm", new { addr = addressOrName, count }, ct);
            return ExtractStringResult(json);
        }

        /// <summary>
        /// Trích xuất danh sách cross-references (xrefs) trỏ tới địa chỉ chỉ định.
        /// </summary>
        public async Task<List<IdaXrefInfo>> GetXrefsToAsync(string address, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "xrefs_to", new { ea = address }, ct);
            var list = new List<IdaXrefInfo>();

            if (json.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in json.EnumerateArray())
                {
                    var xref = new IdaXrefInfo();
                    if (item.TryGetProperty("from_ea", out var fromEa))
                        xref.FromAddress = fromEa.GetString() ?? fromEa.ToString();
                    if (item.TryGetProperty("to_ea", out var toEa))
                        xref.ToAddress = toEa.GetString() ?? toEa.ToString();
                    if (item.TryGetProperty("type", out var type)) xref.Type = type.GetString() ?? "";
                    if (item.TryGetProperty("func_name", out var fn)) xref.FunctionName = fn.GetString() ?? "";
                    list.Add(xref);
                }
            }

            return list;
        }

        /// <summary>
        /// Lấy cây callgraph của một hàm.
        /// </summary>
        public async Task<IdaCallgraphInfo> GetCallgraphAsync(string address, int depth = 2,
            string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "callgraph", new { ea = address, depth }, ct);
            var callgraph = new IdaCallgraphInfo
            {
                RootFunction = address,
                RawJson = json.ToString()
            };

            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("callees", out var callees) && callees.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in callees.EnumerateArray()) callgraph.Callees.Add(c.GetString() ?? c.ToString());
                }

                if (json.TryGetProperty("callers", out var callers) && callers.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in callers.EnumerateArray()) callgraph.Callers.Add(c.GetString() ?? c.ToString());
                }
            }

            return callgraph;
        }

        // ==========================================
        // 3. MUTATION (Tác động / Thay đổi - Cần Guardrail)
        // ==========================================

        /// <summary>
        /// Đổi tên hàm tại địa chỉ chỉ định trên IDA Pro.
        /// </summary>
        public async Task<bool> RenameFunctionAsync(string address, string newName, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "rename_function", new { address, new_name = newName },
                ct);
            return IsSuccessResult(json);
        }

        /// <summary>
        /// Đặt comment tại địa chỉ chỉ định trên IDA Pro.
        /// </summary>
        public async Task<bool> SetCommentAsync(string address, string comment, bool isRepeatable = false,
            string toolId = DefaultToolId, CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "set_comment",
                new { ea = address, comment, repeatable = isRepeatable }, ct);
            return IsSuccessResult(json);
        }

        /// <summary>
        /// Patch bytes tại địa chỉ chỉ định trên IDA Pro.
        /// </summary>
        public async Task<bool> PatchBytesAsync(string address, string hexBytes, string toolId = DefaultToolId,
            CancellationToken ct = default)
        {
            var json = await _mcpManager.CallToolAsync(toolId, "patch_bytes",
                new { ea = address, hex_bytes = hexBytes }, ct);
            return IsSuccessResult(json);
        }

        // ==========================================
        // HELPERS
        // ==========================================

        private static IdaFunctionInfo ParseFunctionInfo(JsonElement item)
        {
            var info = new IdaFunctionInfo();
            if (item.TryGetProperty("ea", out var ea)) info.Address = ea.GetString() ?? ea.GetInt64().ToString("X");
            if (item.TryGetProperty("address", out var addr))
                info.Address = addr.GetString() ?? addr.GetInt64().ToString("X");
            if (item.TryGetProperty("name", out var name)) info.Name = name.GetString() ?? "";
            if (item.TryGetProperty("size", out var size)) info.Size = size.GetInt64();
            if (item.TryGetProperty("prototype", out var proto)) info.Prototype = proto.GetString() ?? "";
            return info;
        }

        private static string ExtractStringResult(JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.String)
            {
                return json.GetString() ?? "";
            }

            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("code", out var code)) return code.GetString() ?? "";
                if (json.TryGetProperty("decompilation", out var decomp)) return decomp.GetString() ?? "";
                if (json.TryGetProperty("disasm", out var disasm)) return disasm.GetString() ?? "";
                if (json.TryGetProperty("raw_text", out var rawText)) return rawText.GetString() ?? "";
            }

            return json.ToString();
        }

        private static bool IsSuccessResult(JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("ok", out var ok)) return ok.GetBoolean();
                if (json.TryGetProperty("success", out var success)) return success.GetBoolean();
                if (json.TryGetProperty("is_error", out var isError)) return !isError.GetBoolean();
            }

            return true;
        }
    }
}