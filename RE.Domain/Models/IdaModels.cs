using System;
using System.Collections.Generic;

namespace RE.Domain.Models
{
    public sealed class IdaMetadata
    {
        public string InputFile { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public int Bits { get; set; }
        public string Endian { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
    }

    public sealed class IdaCursorInfo
    {
        public string Address { get; set; } = string.Empty;
        public string FunctionAddress { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
        public string DisassemblyLine { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
    }

    public sealed class IdaFunctionInfo
    {
        public string Address { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Prototype { get; set; } = string.Empty;
        public List<string> Flags { get; set; } = new();
    }

    public sealed class IdaXrefInfo
    {
        public string FromAddress { get; set; } = string.Empty;
        public string ToAddress { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
    }

    public sealed class IdaCallgraphInfo
    {
        public string RootFunction { get; set; } = string.Empty;
        public List<string> Callees { get; set; } = new();
        public List<string> Callers { get; set; } = new();
        public string RawJson { get; set; } = string.Empty;
    }
}