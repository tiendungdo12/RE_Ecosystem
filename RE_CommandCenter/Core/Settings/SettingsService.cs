using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace RE_CommandCenter.Core.Settings
{
    public static class SettingsService
    {
        public static GlobalSettings Current { get; private set; } = new GlobalSettings();
        
        public static event EventHandler? SettingsChanged;

        private static string GetSettingsFilePath()
        {
            return Path.Combine(AgentConfigLoader.GetConfigFolderPath(), "Global_Settings.json");
        }

        public static async Task LoadAsync()
        {
            string path = GetSettingsFilePath();
            if (File.Exists(path))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(path);
                    var settings = JsonSerializer.Deserialize<GlobalSettings>(json);
                    if (settings != null)
                    {
                        Current = settings;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
                }
            }
        }

        public static async Task SaveAsync()
        {
            // Update UI immediately (optimistic update)
            NotifySettingsChanged();
            
            string path = GetSettingsFilePath();
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Current, options);
                await File.WriteAllTextAsync(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
                try 
                {
                    // Fallback log to see why it fails in dotnet run
                    string errPath = Path.Combine(Path.GetTempPath(), "RE_CommandCenter_Settings_Error.log");
                    await File.WriteAllTextAsync(errPath, $"Path: {path}\nError: {ex.ToString()}");
                } 
                catch { }
            }
        }

        public static void NotifySettingsChanged()
        {
            SettingsChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
