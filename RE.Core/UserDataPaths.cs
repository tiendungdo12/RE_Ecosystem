namespace RE.Core;

/// <summary>Default per-user settings and explicit developer/test data isolation.</summary>
public static class UserDataPaths
{
    public static string? IsolatedRoot
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable("RE_ECOSYSTEM_USER_DATA_ROOT");
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
        }
    }

    public static string SettingsDirectory => Path.Combine(IsolatedRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RE_Ecosystem"), "Settings");
}
