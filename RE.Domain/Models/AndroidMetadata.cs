using System.Collections.Generic;

namespace RE.Domain.Models;

public class AndroidMetadata
{
    public string Format { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string VersionCode { get; set; } = string.Empty;
    public string VersionName { get; set; } = string.Empty;
    public string MainActivity { get; set; } = string.Empty;
    public string? MinSdk { get; set; }
    public string? TargetSdk { get; set; }
    public int PermissionsCount { get; set; }
    public List<AndroidDangerousPermission> DangerousPermissions { get; set; } = new();
    public int DexCount { get; set; }
    public List<string> NativeArchitectures { get; set; } = new();
    public List<string> NativeLibraries { get; set; } = new();
    public string Engine { get; set; } = "unknown";
    public string Packer { get; set; } = "none";
    public bool IsSplitPackage { get; set; }
    public List<string> SplitApks { get; set; } = new();
}

public class AndroidDangerousPermission
{
    public string Name { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}