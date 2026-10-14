namespace RE.Core.Mcp;

/// <summary>Temporary fail-closed baseline. Active capabilities await H2/H8 backend approval.</summary>
public static class PreH0ToolPolicy
{
    private static readonly IReadOnlyDictionary<string, HashSet<string>> Allowed =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["static.md5"] = new(StringComparer.Ordinal) { "md5_file" },
            ["interactive.ida"] = new(StringComparer.Ordinal)
            {
                "get_metadata", "get_cursor", "list_functions", "decompile", "disasm", "xrefs_to", "callgraph"
            },
            ["network.burp"] = new(StringComparer.Ordinal)
            {
                "get_proxy_http_history", "get_proxy_http_history_regex", "get_proxy_websocket_history",
                "get_scanner_issues", "get_collaborator_interactions", "get_active_editor_contents",
                "url_encode", "url_decode", "base64_encode", "base64_decode"
            }
        };

    public static void EnsureAllowed(string toolId, string toolName)
    {
        if (!Allowed.TryGetValue(toolId, out var tools) || !tools.Contains(toolName))
            throw new ToolPolicyDeniedException("Tool invocation denied: only reviewed read-only PRE-H0 tools are enabled. " +
                "Active and unknown tools require backend scope-bound approval, which is not implemented yet.");
    }
}

public sealed class ToolPolicyDeniedException(string message) : InvalidOperationException(message);
