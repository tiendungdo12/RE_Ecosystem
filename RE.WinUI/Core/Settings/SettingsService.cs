using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using RE.Core;
using RE.Infrastructure.Persistence;
using RE.Platform.Windows;

namespace RE.WinUI.Core.Settings
{
    public static class SettingsService
    {
        public static GlobalSettings Current { get; private set; } = new GlobalSettings();

        public static event EventHandler? SettingsChanged;
        public static event EventHandler? SettingsError;
        public static string LastError { get; private set; } = string.Empty;
        private static readonly System.Threading.SemaphoreSlim Gate = new(1, 1);
        private static readonly DpapiSecretStore Secrets = new();
        private static readonly UserSettingsStore Store = new(GetSettingsFilePath(), Secrets);
        private static bool _loaded;

        private static string GetSettingsFilePath()
        {
            return Path.Combine(UserDataPaths.SettingsDirectory, "Global_Settings.json");
        }

        public static async Task LoadAsync()
        {
            await Gate.WaitAsync();
            try
            {
                string template = Path.Combine(AgentConfigLoader.GetConfigFolderPath(), "Global_Settings.json");
                string json = await Store.LoadAsync(template);
                var settings = JsonSerializer.Deserialize<GlobalSettings>(json)
                    ?? throw new JsonException("Invalid settings.");
                foreach (var key in settings.ApiKeys)
                {
                    if (!string.IsNullOrEmpty(key.SecretReference))
                        key.Key = await Secrets.ReadAsync(key.SecretReference)
                            ?? throw new IOException("A stored credential is missing. Re-enter it after restoring settings.");
                }
                Current = settings;
                _loaded = true;
                LastError = string.Empty;
            }
            catch (Exception ex) { ReportError("Settings could not be loaded; existing data has been preserved.", ex); }
            finally { Gate.Release(); }
            ApplyLanguage(Current.AppLanguage);
        }

        public static string ResolveLanguageTag(string? lang)
        {
            if (string.IsNullOrWhiteSpace(lang) || lang.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var languages = Windows.System.UserProfile.GlobalizationPreferences.Languages;
                    if (languages != null && languages.Count > 0)
                    {
                        var topLang = languages[0];
                        if (topLang.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
                        {
                            return "vi-VN";
                        }

                        if (topLang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        {
                            return "en-US";
                        }

                        return topLang;
                    }
                }
                catch
                {
                }

                try
                {
                    var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
                    if (culture.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
                    {
                        return "vi-VN";
                    }

                    if (culture.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    {
                        return "en-US";
                    }

                    if (!string.IsNullOrWhiteSpace(culture))
                    {
                        return culture;
                    }
                }
                catch
                {
                }

                return "en-US";
            }

            return lang;
        }

        public static void ApplyLanguage(string? lang)
        {
            try
            {
                string tag = ResolveLanguageTag(lang);
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set PrimaryLanguageOverride: {ex.Message}");
            }
        }

        public static async Task SaveAsync()
        {
            NotifySettingsChanged();
            await Gate.WaitAsync();
            try
            {
                EnsureLoaded();
                await Store.SaveAsync(JsonSerializer.Serialize(Current));
                LastError = string.Empty;
            }
            catch (Exception ex) { ReportError("Settings could not be saved; the previous file is intact.", ex); }
            finally { Gate.Release(); }
        }

        public static async Task SaveApiKeyAsync(string provider, string value)
        {
            await Gate.WaitAsync();
            try
            {
                EnsureLoaded();
                var copy = JsonSerializer.Deserialize<GlobalSettings>(JsonSerializer.Serialize(Current))!;
                var key = copy.ApiKeys.Find(k => string.Equals(k.Provider, provider, StringComparison.OrdinalIgnoreCase));
                string oldReference = key?.SecretReference ?? string.Empty;
                if (key == null) { key = new ApiKeyItem { Provider = provider }; copy.ApiKeys.Add(key); }
                string json = await Store.SaveAsync(JsonSerializer.Serialize(copy),
                    new System.Collections.Generic.Dictionary<string, string> { [key.Id] = value });
                var saved = JsonSerializer.Deserialize<GlobalSettings>(json)!;
                foreach (var item in saved.ApiKeys)
                    item.Key = item.Id == key.Id ? value : Current.ApiKeys.Find(k => k.Id == item.Id)?.Key ?? string.Empty;
                Current.ApiKeys = saved.ApiKeys;
                if (!string.IsNullOrEmpty(oldReference)) await Secrets.DeleteAsync(oldReference);
                LastError = string.Empty;
                NotifySettingsChanged();
            }
            catch (Exception ex) { ReportError("The API key could not be saved securely.", ex); throw; }
            finally { Gate.Release(); }
        }

        public static async Task DeleteApiKeyAsync(string id)
        {
            await Gate.WaitAsync();
            try
            {
                EnsureLoaded();
                var key = Current.ApiKeys.Find(k => k.Id == id);
                if (key == null) return;
                var copy = JsonSerializer.Deserialize<GlobalSettings>(JsonSerializer.Serialize(Current))!;
                copy.ApiKeys.RemoveAll(k => k.Id == id);
                await Store.SaveAsync(JsonSerializer.Serialize(copy));
                Current.ApiKeys.Remove(key);
                if (!string.IsNullOrEmpty(key.SecretReference)) await Secrets.DeleteAsync(key.SecretReference);
                LastError = string.Empty;
                NotifySettingsChanged();
            }
            catch (Exception ex) { ReportError("The API key could not be removed securely.", ex); throw; }
            finally { Gate.Release(); }
        }

        private static void EnsureLoaded()
        {
            if (!_loaded) throw new InvalidOperationException("Settings load failed; refusing to overwrite existing data.");
        }

        private static void ReportError(string message, Exception exception)
        {
            LastError = message;
            System.Diagnostics.Debug.WriteLine(message + " (" + exception.GetType().Name + ")"); // Never log credential payloads.
            SettingsError?.Invoke(null, EventArgs.Empty);
        }

        public static void NotifySettingsChanged()
        {
            SettingsChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
