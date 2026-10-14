using System;
using System.Collections.Generic;

namespace RE.Domain.Models
{
    public sealed class BurpHttpHistoryItem
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public long RequestLength { get; set; }
        public long ResponseLength { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
    }

    public sealed class BurpScannerIssue
    {
        public string IssueName { get; set; } = string.Empty;
        public string IssueType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Confidence { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string IssueBackground { get; set; } = string.Empty;
        public string IssueDetail { get; set; } = string.Empty;
        public string RemediationBackground { get; set; } = string.Empty;
        public string RemediationDetail { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
    }

    public sealed class BurpCollaboratorPayload
    {
        public string PayloadUrl { get; set; } = string.Empty;
        public string PayloadId { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
    }

    public sealed class BurpCollaboratorInteraction
    {
        public string InteractionId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string ClientIp { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string ProtocolData { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
    }

    public sealed class BurpOrganizerItem
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
    }

    public sealed class BurpHttpRequestResult
    {
        public int StatusCode { get; set; }
        public string ResponseHeaders { get; set; } = string.Empty;
        public string ResponseBody { get; set; } = string.Empty;
        public long ResponseTimeMs { get; set; }
        public string RawJson { get; set; } = string.Empty;
    }
}