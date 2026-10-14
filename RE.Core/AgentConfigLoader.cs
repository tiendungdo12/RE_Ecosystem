using System;
using System.IO;
using System.Threading.Tasks;

namespace RE.Core
{
    public static class AgentConfigLoader
    {
        // Tim root cua app trong ca hai che do:
        // Dev: di nguoc tu bin/Debug den noi co RE.WinUI.csproj.
        // Packaged/output: dung AppDomain.CurrentDomain.BaseDirectory.
        public static string GetAppRootPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo? dir = new DirectoryInfo(baseDir);

            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "RE.WinUI.csproj")))
                {
                    return dir.FullName;
                }

                if (Directory.Exists(Path.Combine(dir.FullName, "RE.WinUI")) &&
                    File.Exists(Path.Combine(dir.FullName, "RE.WinUI", "RE.WinUI.csproj")))
                {
                    return Path.Combine(dir.FullName, "RE.WinUI");
                }

                dir = dir.Parent;
            }

            return baseDir;
        }

        public static string GetSolutionRootPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo? dir = new DirectoryInfo(baseDir);

            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "RE_Ecosystem.slnx")) ||
                    File.Exists(Path.Combine(dir.FullName, "RE_Ecosystem.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            string appRoot = GetAppRootPath();
            return Directory.GetParent(appRoot)?.FullName ?? appRoot;
        }

        public static string GetConfigFolderPath()
        {
            return Path.Combine(GetAppRootPath(), "AgentConfigs");
        }

        // IDENTITY.md la phan "nhan cach/vai tro" cua agent trong Giai doan 1.
        public static async Task<string> LoadIdentityAsync()
        {
            string filePath = Path.Combine(GetConfigFolderPath(), "IDENTITY.md");
            try
            {
                if (File.Exists(filePath))
                {
                    return await File.ReadAllTextAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading IDENTITY.md: {ex.Message}");
            }

            return string.Empty;
        }

        // ROUTING_RULES.yaml ban dau duoc dua vao system prompt.
        // Tu Block 2.4 tro di, file nay se tien hoa thanh bang dieu phoi tool that.
        public static async Task<string> LoadRoutingRulesAsync()
        {
            string filePath = Path.Combine(GetConfigFolderPath(), "ROUTING_RULES.yaml");
            try
            {
                if (File.Exists(filePath))
                {
                    return await File.ReadAllTextAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading ROUTING_RULES.yaml: {ex.Message}");
            }

            return string.Empty;
        }
    }
}