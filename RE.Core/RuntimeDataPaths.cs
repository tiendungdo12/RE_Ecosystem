using System;
using System.Collections.Generic;
using System.IO;

namespace RE.Core;

/// <summary>
/// Locations used by external AI runtimes.
///
/// During development, the state is deliberately kept in the workspace so it
/// is easy to inspect and reset. A packaged build falls back to a per-user
/// directory because an installed application directory may be read-only.
/// </summary>
public static class RuntimeDataPaths
{
    private const string WorkspaceMarker = "RE_Ecosystem.slnx";
    private const string RuntimeFolderName = ".ai_data";

    public static string WorkspacePath => FindWorkspaceRoot() ?? AppDomain.CurrentDomain.BaseDirectory;

    public static string RootPath => GetOrCreateRootPath();

    public static string AntigravityProfilePath => GetOrCreateProviderPath("antigravity", "default");

    public static string CodexHomePath => GetOrCreateProviderPath("codex", "default");

    public static string GetOrCreateProviderPath(string provider, string profile)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));

        if (string.IsNullOrWhiteSpace(profile))
            throw new ArgumentException("Profile is required.", nameof(profile));

        string path = Path.Combine(RootPath, SanitizePathPart(provider), SanitizePathPart(profile));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetOrCreateRootPath()
    {
        string? workspace = FindWorkspaceRoot();
        string root = workspace != null
            ? Path.Combine(workspace, RuntimeFolderName)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RE_Ecosystem",
                RuntimeFolderName);

        Directory.CreateDirectory(root);
        return root;
    }

    private static string? FindWorkspaceRoot()
    {
        var candidates = new List<string>
        {
            AppDomain.CurrentDomain.BaseDirectory,
            Environment.CurrentDirectory
        };

        foreach (string candidate in candidates)
        {
            DirectoryInfo? directory = new DirectoryInfo(candidate);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, WorkspaceMarker)) ||
                    Directory.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static string SanitizePathPart(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value.Trim();
    }
}