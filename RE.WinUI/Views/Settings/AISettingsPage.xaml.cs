using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RE.Core;
using RE.Core.Services;
using RE.Domain.Models;
using RE.Infrastructure.AI;
using RE.WinUI.Core.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RE.WinUI.Views.Settings
{
    public sealed partial class AISettingsPage : Page
    {
        private bool _isInitialized = false;
        private bool _antigravityAuthenticated;
        private string? _antigravityAccountDisplay;
        private string? _antigravitySignInError;
        private AntigravityUsageSnapshot? _usageSnapshot;

        public ObservableCollection<QuotaBucketDisplayItem> QuotaBuckets { get; } = new();
        public ObservableCollection<OllamaModelDisplayItem> InstalledOllamaModels { get; } = new();
        public ObservableCollection<ApiKeyDisplayItem> ConfiguredApiKeys { get; } = new();

        public AISettingsPage()
        {
            InitializeComponent();
            Loaded += AISettingsPage_Loaded;
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double newWidth = Math.Min(800, e.NewSize.Width);
            if (MainStackPanel != null) MainStackPanel.Width = newWidth;
        }

        private void AISettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = SettingsService.Current;

                OllamaPortTextBox.Text = settings.OllamaPort ?? "11434";

                _antigravityAuthenticated = settings.IsAntigravityLoggedIn;
                _antigravityAccountDisplay = string.IsNullOrWhiteSpace(settings.AntigravityAccountEmail)
                    ? null
                    : settings.AntigravityAccountEmail;

                LoadCachedOrConfiguredOllamaModels();
                LoadApiKeys();
                UpdateLanguage();

                _isInitialized = true;

                _ = RefreshAntigravityStatusAsync();
                _ = RefreshAntigravityQuotaAsync();
                _ = LoadOllamaModelsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AISettingsPage_Loaded error: {ex}");
            }
        }

        public void UpdateLanguage()
        {
            try
            {
                DetailTitle.Text = GetLocalizedString("AISettingsTitle.Text", "AI & Models");

                AntigravityExpander.Header = GetLocalizedString("AntigravityExpander.Header", "Antigravity");
                AntigravityExpander.Description =
                    GetLocalizedString("AntigravityExpander.Description", "Google Deepmind");
                AntigravityQuotaHeaderCard.Header = GetLocalizedString("AntigravityQuotaTitle", "Usage Quota");
                AntigravityRefreshQuotaText.Text = GetLocalizedString("AntigravityRefreshQuota", "Refresh Quota");

                OllamaExpander.Header = GetLocalizedString("OllamaExpander.Header", "Ollama");
                OllamaExpander.Description = GetLocalizedString("OllamaExpander.Description", "Local and Cloud Models");
                OllamaViewUsageButton.Content =
                    GetLocalizedString("OllamaViewUsageButton.Content", "View Usage on Web");
                OllamaPortCard.Header = GetLocalizedString("OllamaPortCard.Header", "Ollama Port");
                OllamaModelsHeaderCard.Header = GetLocalizedString("OllamaModelsCard.Header", "Installed models");
                OllamaModelsHeaderCard.Description = GetLocalizedString("OllamaModelsCard.Description",
                    "Manage and download local Ollama models");
                OllamaAddModelButtonText.Text = GetLocalizedString("OllamaAddModelButton.Content", "Add Model");
                OllamaNoModelsCard.Header = GetLocalizedString("OllamaNoModelsText",
                    "No models installed. Click Add Model to pull a model.");

                ApiKeysExpander.Header = GetLocalizedString("ApiKeysExpander.Header", "API keys & Providers");
                ApiKeysExpander.Description = GetLocalizedString("ApiKeysExpander.Description",
                    "Manage API keys for cloud models and web search services");
                AddApiKeyButtonText.Text = GetLocalizedString("AddApiKeyButton.Content", "Add API Key");
                NoApiKeysCard.Header = GetLocalizedString("NoApiKeysConfigured",
                    "No API keys added. Click Add API Key to configure cloud providers.");

                ApplyAntigravityAccountStatus();
                RenderQuotaBuckets();
                RenderOllamaModels();
                RenderApiKeys();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateLanguage error: {ex}");
            }
        }

        private static string GetLocalizedString(string key, string fallback)
        {
            try
            {
                var loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
                string val = loader.GetString(key);
                if (string.IsNullOrWhiteSpace(val) && key.Contains('.'))
                {
                    val = loader.GetString(key.Replace('.', '/'));
                }

                return string.IsNullOrWhiteSpace(val) ? fallback : val;
            }
            catch
            {
                try
                {
                    if (key.Contains('.'))
                    {
                        var val =
                            new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader().GetString(key.Replace('.',
                                '/'));
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                    else if (key.Contains('/'))
                    {
                        var val =
                            new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader().GetString(key.Replace('/',
                                '.'));
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }
                catch
                {
                }

                return fallback;
            }
        }

        #region Antigravity Quota & Status

        private void ApplyAntigravityAccountStatus()
        {
            if (!_antigravityAuthenticated)
            {
                AntigravityAccountStatusText.Text = _antigravitySignInError ??
                                                    GetLocalizedString("AntigravityAccountStatusText_NotLoggedIn",
                                                        "Not logged in. Log in via Antigravity Desktop app.");
                return;
            }

            AntigravityAccountStatusText.Text = _antigravityAccountDisplay ??
                                                GetLocalizedString("AntigravityAccountStatusText_LoggedIn",
                                                    "Logged in via Desktop app");
        }

        private async Task RefreshAntigravityStatusAsync()
        {
            try
            {
                AntigravityAccountStatusProgress.IsActive = true;
                AntigravityAccountStatusProgress.Visibility = Visibility.Visible;
                if (!_antigravityAuthenticated)
                {
                    AntigravityAccountStatusText.Text = GetLocalizedString("AntigravityAccountStatusText_Checking",
                        "Checking account status...");
                }

                var info = await new AntigravityLanguageServerClient().GetAccountInfoAsync();
                _antigravityAuthenticated = info.IsAuthenticated;
                var accountStr = !string.IsNullOrWhiteSpace(info.Email) ? info.Email : info.DisplayName;
                _antigravityAccountDisplay = string.IsNullOrWhiteSpace(info.PlanName)
                    ? accountStr
                    : $"{accountStr} ({info.PlanName})";

                if (SettingsService.Current.IsAntigravityLoggedIn != _antigravityAuthenticated ||
                    SettingsService.Current.AntigravityAccountEmail != _antigravityAccountDisplay)
                {
                    SettingsService.Current.IsAntigravityLoggedIn = _antigravityAuthenticated;
                    SettingsService.Current.AntigravityAccountEmail = _antigravityAccountDisplay ?? string.Empty;
                    await SettingsService.SaveAsync();
                }

                _antigravitySignInError = null;
                ApplyAntigravityAccountStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Antigravity status refresh failed: {ex.Message}");
                _antigravityAuthenticated = SettingsService.Current.IsAntigravityLoggedIn;
                ApplyAntigravityAccountStatus();
            }
            finally
            {
                AntigravityAccountStatusProgress.IsActive = false;
                AntigravityAccountStatusProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void RefreshAntigravityQuota_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshAntigravityQuotaAsync();
        }

        private async Task RefreshAntigravityQuotaAsync()
        {
            try
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    AntigravityQuotaProgress.IsActive = true;
                    AntigravityQuotaProgress.Visibility = Visibility.Visible;
                });

                var usage = await new AntigravityLanguageServerClient().GetUsageAsync();
                _usageSnapshot = usage;
                DispatcherQueue.TryEnqueue(RenderQuotaBuckets);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Antigravity quota refresh failed: {ex.Message}");
                _usageSnapshot = null;
                DispatcherQueue.TryEnqueue(() =>
                {
                    QuotaBuckets.Clear();
                    RenderQuotaBuckets();
                });
            }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    AntigravityQuotaProgress.IsActive = false;
                    AntigravityQuotaProgress.Visibility = Visibility.Collapsed;
                });
            }
        }

        private void RenderQuotaBuckets()
        {
            QuotaBuckets.Clear();

            if (_usageSnapshot == null || _usageSnapshot.Groups.Count == 0)
            {
                return;
            }

            string remainingFormat = GetLocalizedString("UsageRemainingFormat", "{0}% remaining");

            foreach (var group in _usageSnapshot.Groups)
            {
                string groupName = LocalizeUsageGroup(group.DisplayName);

                foreach (var bucket in group.Buckets.OrderBy(bucket =>
                             bucket.Window.Equals("5h", StringComparison.OrdinalIgnoreCase) ? 0 :
                             bucket.Window.Equals("weekly", StringComparison.OrdinalIgnoreCase) ? 1 : 2))
                {
                    string bucketTitle = $"{groupName} · {LocalizeUsageBucket(bucket)}";
                    string resetText = bucket.ResetTime.HasValue
                        ? string.Format(GetLocalizedString("UsageResetAt", "Resets at {0}"),
                            bucket.ResetTime.Value.ToLocalTime().ToString("g"))
                        : "";

                    QuotaBuckets.Add(new QuotaBucketDisplayItem
                    {
                        DisplayName = bucketTitle,
                        RemainingPercent = bucket.RemainingPercent,
                        RemainingPercentText = string.Format(remainingFormat, bucket.RemainingPercent.ToString("0")),
                        ResetTimeText = resetText
                    });
                }
            }
        }

        private void RenderOllamaModels()
        {
            if (OllamaNoModelsCard == null) return;
            OllamaNoModelsCard.Visibility =
                InstalledOllamaModels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RenderApiKeys()
        {
            if (NoApiKeysCard == null) return;
            NoApiKeysCard.Visibility = ConfiguredApiKeys.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private string LocalizeUsageGroup(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return GetLocalizedString("UsageGeminiModels", "Gemini Models");
            if (name.Contains("Claude", StringComparison.OrdinalIgnoreCase) &&
                name.Contains("GPT", StringComparison.OrdinalIgnoreCase))
                return GetLocalizedString("UsageClaudeGptModels", "Claude and GPT models");
            if (name.Contains("Gemini", StringComparison.OrdinalIgnoreCase))
                return GetLocalizedString("UsageGeminiModels", "Gemini Models");
            return name;
        }

        private string LocalizeUsageBucket(AntigravityUsageBucket bucket)
        {
            if (bucket.Window.Equals("weekly", StringComparison.OrdinalIgnoreCase))
                return GetLocalizedString("UsageWeeklyLimitRemaining", "Weekly Limit Remaining");
            if (bucket.Window.Equals("5h", StringComparison.OrdinalIgnoreCase))
                return GetLocalizedString("UsageFiveHourLimitRemaining", "Five Hour Limit Remaining");
            return bucket.DisplayName;
        }

        #endregion

        #region Ollama Models Management

        private void OllamaPortTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            SettingsService.Current.OllamaPort = OllamaPortTextBox.Text;
            _ = SettingsService.SaveAsync();
        }

        private void OllamaViewUsageButton_Click(object sender, RoutedEventArgs e)
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://ollama.com/settings"));
        }

        private void LoadCachedOrConfiguredOllamaModels()
        {
            InstalledOllamaModels.Clear();
            var settings = SettingsService.Current;

            // 1. Load from cache
            if (!string.IsNullOrWhiteSpace(settings.OllamaCachedModelsJson) && settings.OllamaCachedModelsJson != "[]")
            {
                try
                {
                    var cached =
                        System.Text.Json.JsonSerializer.Deserialize<List<string>>(settings.OllamaCachedModelsJson);
                    if (cached != null && cached.Count > 0)
                    {
                        foreach (var m in cached.Where(x => !string.IsNullOrWhiteSpace(x)))
                        {
                            InstalledOllamaModels.Add(new OllamaModelDisplayItem
                            {
                                Name = m,
                                SizeFormatted = ""
                            });
                        }
                    }
                }
                catch
                {
                }
            }

            // 2. Fallback to active Ollama model if empty
            if (InstalledOllamaModels.Count == 0 && !string.IsNullOrWhiteSpace(settings.OllamaModel))
            {
                InstalledOllamaModels.Add(new OllamaModelDisplayItem
                {
                    Name = settings.OllamaModel,
                    SizeFormatted = ""
                });
            }

            RenderOllamaModels();
        }

        private async Task LoadOllamaModelsAsync()
        {
            try
            {
                var client = new OllamaClient();
                var models = await client.GetInstalledModelsDetailsAsync(SettingsService.Current.OllamaPort ?? "11434");

                if (models.Count > 0)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        InstalledOllamaModels.Clear();
                        foreach (var model in models)
                        {
                            InstalledOllamaModels.Add(new OllamaModelDisplayItem
                            {
                                Name = model.Name,
                                SizeFormatted = model.SizeFormatted
                            });
                        }

                        RenderOllamaModels();
                    });

                    SettingsService.Current.OllamaCachedModelsJson =
                        System.Text.Json.JsonSerializer.Serialize(models.Select(m => m.Name).ToList());
                    await SettingsService.SaveAsync();
                }
                else
                {
                    DispatcherQueue.TryEnqueue(RenderOllamaModels);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load Ollama models: {ex.Message}");
                DispatcherQueue.TryEnqueue(RenderOllamaModels);
            }
        }

        public async void DeleteModel_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string modelName && !string.IsNullOrWhiteSpace(modelName))
            {
                string title = GetLocalizedString("DeleteModelDialogTitle", "Delete Model");
                string contentFormat = GetLocalizedString("DeleteModelDialogContent",
                    "Are you sure you want to delete model '{0}'? This will free up disk space.");
                string primaryBtn = GetLocalizedString("DeleteModelButton", "Delete");
                string closeBtn = GetLocalizedString("CancelButton", "Cancel");

                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = string.Format(contentFormat, modelName),
                    PrimaryButtonText = primaryBtn,
                    CloseButtonText = closeBtn,
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var client = new OllamaClient();
                    bool deleted =
                        await client.DeleteModelAsync(modelName, SettingsService.Current.OllamaPort ?? "11434");

                    var target = InstalledOllamaModels.FirstOrDefault(m => m.Name == modelName);
                    if (target != null) InstalledOllamaModels.Remove(target);

                    var cachedList = InstalledOllamaModels.Select(m => m.Name).ToList();
                    SettingsService.Current.OllamaCachedModelsJson =
                        System.Text.Json.JsonSerializer.Serialize(cachedList);
                    await SettingsService.SaveAsync();
                    RenderOllamaModels();
                }
            }
        }

        private async void AddOllamaModelButton_Click(object sender, RoutedEventArgs e)
        {
            var inputTextBox = new TextBox
            {
                PlaceholderText =
                    GetLocalizedString("AddOllamaModelPlaceholder", "Enter model name (e.g. llama3, qwen2.5)"),
                MinWidth = 320,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var contentPanel = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(0, 12, 0, 0),
                Children = { inputTextBox }
            };
            var dialog = new ContentDialog
            {
                Title = GetLocalizedString("AddOllamaModelTitle", "Add Ollama model"),
                Content = contentPanel,
                PrimaryButtonText = GetLocalizedString("CheckAddButton", "Check/Add"),
                CloseButtonText = GetLocalizedString("CancelButton", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
            {
                string modelName = inputTextBox.Text.Trim();

                var progressDialog = new ContentDialog
                {
                    Title = GetLocalizedString("PullingModelTitle", "Pulling model"),
                    Content = new StackPanel
                    {
                        Spacing = 16,
                        Children =
                        {
                            new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center },
                            new TextBlock
                            {
                                Text = string.Format(
                                    GetLocalizedString("RunningOllamaPull", "Running ollama pull {0}..."), modelName),
                                HorizontalAlignment = HorizontalAlignment.Center
                            }
                        }
                    },
                    XamlRoot = this.XamlRoot,
                    CloseButtonText = GetLocalizedString("CancelButton", "Cancel")
                };

                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "ollama",
                        Arguments = $"pull {modelName}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var process = Process.Start(startInfo);

                    progressDialog.Closed += (s, args) =>
                    {
                        if (process != null && !process.HasExited)
                        {
                            try
                            {
                                process.Kill(true);
                            }
                            catch
                            {
                            }
                        }
                    };

                    _ = progressDialog.ShowAsync();

                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        progressDialog.Hide();

                        if (process.ExitCode == 0)
                        {
                            await LoadOllamaModelsAsync();
                        }
                        else
                        {
                            // Add to list as configured even if pull had warning
                            if (!InstalledOllamaModels.Any(m =>
                                    m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase)))
                            {
                                InstalledOllamaModels.Add(new OllamaModelDisplayItem
                                    { Name = modelName, SizeFormatted = "" });
                                var cachedList = InstalledOllamaModels.Select(m => m.Name).ToList();
                                SettingsService.Current.OllamaCachedModelsJson =
                                    System.Text.Json.JsonSerializer.Serialize(cachedList);
                                await SettingsService.SaveAsync();
                                OllamaNoModelsCard.Visibility = Visibility.Collapsed;
                            }
                        }
                    }
                    else
                    {
                        progressDialog.Hide();
                    }
                }
                catch (Exception ex)
                {
                    progressDialog.Hide();
                    // Add to list as configured
                    if (!InstalledOllamaModels.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase)))
                    {
                        InstalledOllamaModels.Add(new OllamaModelDisplayItem { Name = modelName, SizeFormatted = "" });
                        var cachedList = InstalledOllamaModels.Select(m => m.Name).ToList();
                        SettingsService.Current.OllamaCachedModelsJson =
                            System.Text.Json.JsonSerializer.Serialize(cachedList);
                        await SettingsService.SaveAsync();
                        OllamaNoModelsCard.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        #endregion

        #region API Keys Management

        private void LoadApiKeys()
        {
            ConfiguredApiKeys.Clear();
            var keys = SettingsService.Current.ApiKeys;
            foreach (var key in keys)
            {
                ConfiguredApiKeys.Add(new ApiKeyDisplayItem
                {
                    Id = key.Id,
                    Provider = key.Provider,
                    Key = key.Key,
                    MaskedKey = MaskApiKey(key.Key)
                });
            }

            RenderApiKeys();
        }

        private static string MaskApiKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            if (key.Length <= 8) return "••••••••";
            return $"{key[..4]}••••••••{key[^4..]}";
        }

        private async void AddApiKeyButton_Click(object sender, RoutedEventArgs e)
        {
            var providerComboBox = new ComboBox
            {
                Header = GetLocalizedString("ApiKeyProviderLabel", "Provider"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 0
            };
            providerComboBox.Items.Add("Ollama Cloud");
            providerComboBox.Items.Add("OpenAI");
            providerComboBox.Items.Add("Anthropic (Claude)");
            providerComboBox.Items.Add("Google AI Studio");
            providerComboBox.Items.Add("Groq");
            providerComboBox.Items.Add("Custom");

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(providerComboBox, "ApiKeyProviderInput");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(providerComboBox, "API key provider");
            var keyBox = new PasswordBox
            {
                Header = "API Key / Token",
                PlaceholderText = GetLocalizedString("ApiKeyPlaceholder", "Enter API key / bearer token"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(keyBox, "ApiKeySecretInput");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(keyBox, "API key or token");

            var contentPanel = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(0, 12, 0, 0),
                MinWidth = 340,
                Children = { providerComboBox, keyBox }
            };

            var dialog = new ContentDialog
            {
                Title = GetLocalizedString("AddApiKeyDialogTitle", "Add API Key"),
                Content = contentPanel,
                PrimaryButtonText = GetLocalizedString("SaveButton", "Save"),
                CloseButtonText = GetLocalizedString("CancelButton", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(keyBox.Password))
            {
                string selectedProvider = providerComboBox.SelectedItem?.ToString() ?? "Custom";
                string inputKey = keyBox.Password.Trim();

                try { await SettingsService.SaveApiKeyAsync(selectedProvider, inputKey); }
                catch { /* App displays SettingsService.SettingsError without logging the key. */ }
                keyBox.Password = string.Empty;
                LoadApiKeys();
            }
        }

        public async void DeleteApiKey_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string keyId && !string.IsNullOrWhiteSpace(keyId))
            {
                var target = SettingsService.Current.ApiKeys.FirstOrDefault(k => k.Id == keyId);
                if (target == null) return;

                string title = GetLocalizedString("RemoveApiKeyDialogTitle", "Remove API Key");
                string contentFormat = GetLocalizedString("RemoveApiKeyDialogContent",
                    "Are you sure you want to remove the API key for '{0}'?");
                string primaryBtn = GetLocalizedString("DeleteModelButton", "Delete");
                string closeBtn = GetLocalizedString("CancelButton", "Cancel");

                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = string.Format(contentFormat, target.Provider),
                    PrimaryButtonText = primaryBtn,
                    CloseButtonText = closeBtn,
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    try { await SettingsService.DeleteApiKeyAsync(target.Id); }
                    catch { /* App displays the secure-store error. */ }
                    LoadApiKeys();
                }
            }
        }

        #endregion
    }

    public class QuotaBucketDisplayItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public double RemainingPercent { get; set; }
        public string RemainingPercentText { get; set; } = string.Empty;
        public string ResetTimeText { get; set; } = string.Empty;
    }

    public class OllamaModelDisplayItem
    {
        public string Name { get; set; } = string.Empty;
        public string SizeFormatted { get; set; } = string.Empty;
    }

    public class ApiKeyDisplayItem
    {
        public string Id { get; set; } = string.Empty;
        public string DeleteAutomationId => "DeleteApiKey_" + Id;
        public string Provider { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string MaskedKey { get; set; } = string.Empty;
    }
}
