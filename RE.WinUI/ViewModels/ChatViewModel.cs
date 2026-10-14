using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RE.Core;
using RE.Core.Abstractions;
using RE.Core.Mcp;
using RE.Core.Routing;
using RE.Core.StaticAnalysis;
using RE.Core.Services;
using RE.Domain.Models;
using RE.Application.UseCases;
using RE.Application.Models;
using RE.Infrastructure.AI;
using RE.Infrastructure.Persistence;
using RE.Presentation.ViewModels;
using RE.WinUI.Core.Settings;

namespace RE.WinUI.ViewModels
{
    public partial class ChatViewModel : ObservableObject
    {
        private readonly OllamaClient _ollamaClient;
        private readonly KnowledgeStore _knowledgeStore;
        private readonly ConversationSearchService _conversationSearch;
        private readonly ModelSelectionService _modelSelection;
        private readonly ITriagePipeline _triagePipeline;
        private readonly McpClientManager _mcpManager;
        private string _lastTriageFacts = string.Empty;
        private string _systemPrompt = string.Empty;
        private bool _historyLoaded = false;
        private int _modelLoadGeneration;
        private CancellationTokenSource? _chatCts;
        public ObservableCollection<ChatMessage> ChatHistory { get; } = new ObservableCollection<ChatMessage>();
        public ObservableCollection<ChatSessionInfo> Sessions { get; } = new ObservableCollection<ChatSessionInfo>();
        public ObservableCollection<SessionGroup> GroupedSessions { get; } = new ObservableCollection<SessionGroup>();
        public ObservableCollection<ProjectInfo> RegisteredProjects { get; } = new ObservableCollection<ProjectInfo>();

        public ObservableCollection<UsageGroupDisplayModel> UsageGroups { get; } =
            new ObservableCollection<UsageGroupDisplayModel>();

        [ObservableProperty] public partial bool IsNewChat { get; set; } = false;

        public ObservableCollection<string> AvailableProjects { get; } = new ObservableCollection<string>();
        public ObservableCollection<ModelOption> AvailableModels { get; } = new ObservableCollection<ModelOption>();

        public ObservableCollection<ModelProviderGroup> AvailableModelGroups { get; } =
            new ObservableCollection<ModelProviderGroup>();

        private ModelOption? _selectedModelOption;

        public ModelOption? SelectedModel
        {
            get => _selectedModelOption;
            set
            {
                if (SetProperty(ref _selectedModelOption, value) && value != null &&
                    !string.IsNullOrWhiteSpace(value.Name))
                {
                    var settings = SettingsService.Current;
                    settings.LastSelectedProvider = value.Provider;
                    settings.LastSelectedModel = value.Name;
                    if (value.Provider.Equals("Antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.AiProvider = "Antigravity";
                        settings.AntigravityModel = value.Name;
                    }
                    else if (value.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.AiProvider = "Ollama";
                        settings.OllamaModel = value.Name;
                    }

                    _ = SettingsService.SaveAsync();
                }
            }
        }

        private string _localizedGeneralText = "Conversations";

        private string _selectedProjectForNewChat = "No Project";

        public string SelectedProjectForNewChat
        {
            get => _selectedProjectForNewChat;
            set
            {
                if (SetProperty(ref _selectedProjectForNewChat, value))
                {
                    UpdateNewChatProject(value);
                }
            }
        }

        private void UpdateNewChatProject(string projectName)
        {
            if (SelectedSession != null && IsNewChat)
            {
                string targetProject = (projectName == _localizedGeneralText) ? "General" : projectName;
                SelectedSession.ProjectName = targetProject;
                _ = _knowledgeStore.UpdateChatSessionProjectAsync(SelectedSession.SessionId, targetProject);
                RefreshGroupedSessions();
            }
        }

        private string _searchQuery = string.Empty;

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    RefreshGroupedSessions();
                }
            }
        }

        [ObservableProperty] public partial string UserInput { get; set; } = string.Empty;

        [ObservableProperty] public partial bool IsBusy { get; set; }

        [ObservableProperty] public partial bool IsSidebarCollapsed { get; set; } = false;

        [ObservableProperty] public partial string CurrentWorkspace { get; set; } = "General";

        [ObservableProperty] public partial bool IsSelectionMode { get; set; } = false;

        private ChatSessionInfo? _selectedSession;

        public ChatSessionInfo? SelectedSession
        {
            get => _selectedSession;
            set
            {
                var oldId = _selectedSession?.SessionId;
                if (SetProperty(ref _selectedSession, value))
                {
                    if (value != null)
                    {
                        if (value.SessionId != oldId)
                        {
                            _ = LoadChatHistoryAsync();
                        }

                        if (!string.IsNullOrWhiteSpace(value.ProjectName))
                        {
                            CurrentWorkspace = value.ProjectName;
                        }
                    }
                }
            }
        }

        public ChatViewModel(
            KnowledgeStore knowledgeStore,
            ConversationSearchService conversationSearch,
            ModelSelectionService modelSelection,
            ITriagePipeline triagePipeline, McpClientManager mcpManager)
        {
            _ollamaClient = new OllamaClient();
            _knowledgeStore = knowledgeStore;
            _conversationSearch = conversationSearch;
            _modelSelection = modelSelection;
            _triagePipeline = triagePipeline;
            _mcpManager = mcpManager;
        }

        public async Task InitializeAsync()
        {
            var rl = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            try
            {
                _localizedGeneralText = rl.GetString("ChatNoProjectText");
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(_localizedGeneralText)) _localizedGeneralText = "Conversations";

            // 1. FAST LOCAL-FIRST: Load SQLite projects and sessions immediately (takes ~10ms)
            try
            {
                var projectsTask = _knowledgeStore.GetProjectsAsync();
                var sessionsTask = _knowledgeStore.GetChatSessionsAsync();
                await Task.WhenAll(projectsTask, sessionsTask);

                var projects = await projectsTask;
                RegisteredProjects.Clear();
                AvailableProjects.Clear();
                AvailableProjects.Add(_localizedGeneralText);
                foreach (var p in projects)
                {
                    RegisteredProjects.Add(p);
                    AvailableProjects.Add(p.Name);
                }

                var list = await sessionsTask;
                Sessions.Clear();
                foreach (var s in list)
                {
                    Sessions.Add(s);
                }

                _historyLoaded = true;
                RefreshGroupedSessions();

                if (SelectedSession == null && Sessions.Count > 0)
                {
                    SelectedSession = Sessions[0];
                }
                else
                {
                    _ = LoadChatHistoryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Initialize error: {ex.Message}");
            }

            // 2. PARALLEL BACKGROUND: Load Models and Agent Configurations non-blocking
            _ = LoadModelsAsync();
            _ = Task.Run(async () =>
            {
                try
                {
                    string identity = await AgentConfigLoader.LoadIdentityAsync();
                    string rules = await AgentConfigLoader.LoadRoutingRulesAsync();
                    _systemPrompt = $"{identity}\n\nRules:\n{rules}";
                }
                catch
                {
                }
            });
        }

        private static readonly System.Text.RegularExpressions.Regex ModelLevelRegex =
            new(@"^(.+?)\s*\((High|Medium|Low)\)$", System.Text.RegularExpressions.RegexOptions.Compiled);

        public async Task LoadModelsAsync()
        {
            var settings = SettingsService.Current;
            int loadGeneration = ++_modelLoadGeneration;
            var rl = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            string cloudLabel = GetResource(rl, "ModelCloudLabel", "Cloud");
            string localLabel = GetResource(rl, "ModelLocalLabel", "Local");

            // 1. [CACHE-FIRST / INSTANT] Load cached models immediately
            var cachedAntigravity = LoadCachedAntigravityModels(settings);
            var cachedOllama = LoadCachedOllamaModels(settings);

            RebuildAvailableModels(cachedAntigravity, cachedOllama, cloudLabel, localLabel, settings);

            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            // 2. [BACKGROUND REFRESH] Fetch live models in parallel
            _ = Task.Run(async () =>
            {
                try
                {
                    List<AntigravityCascadeModel>? freshAntigravity = null;
                    IReadOnlyList<string>? freshRecommendedLabels = null;
                    if (settings.IsAntigravityLoggedIn)
                    {
                        try
                        {
                            var client = new AntigravityLanguageServerClient();
                            var result = await client.GetCascadeModelsAsync();
                            if (result.Models.Count > 0)
                            {
                                freshAntigravity = result.Models.ToList();
                                freshRecommendedLabels = result.RecommendedLabels;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Antigravity model refresh failed: {ex.Message}");
                        }
                    }

                    List<string>? freshOllama = null;
                    try
                    {
                        string port = settings.OllamaPort ?? "11434";
                        freshOllama = await _ollamaClient.GetModelsAsync(port);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Ollama model refresh failed: {ex.Message}");
                    }

                    dispatcherQueue?.TryEnqueue(() =>
                    {
                        if (loadGeneration != _modelLoadGeneration)
                            return;

                        bool changed = false;

                        if (freshAntigravity != null && freshAntigravity.Count > 0)
                        {
                            string freshJson = JsonSerializer.Serialize(freshAntigravity);
                            if (!string.Equals(settings.AntigravityCachedModelsJson, freshJson,
                                    StringComparison.Ordinal))
                            {
                                settings.AntigravityCachedModelsJson = freshJson;
                                changed = true;
                            }
                        }

                        if (freshOllama != null && freshOllama.Count > 0)
                        {
                            string freshJson = JsonSerializer.Serialize(freshOllama);
                            if (!string.Equals(settings.OllamaCachedModelsJson, freshJson, StringComparison.Ordinal))
                            {
                                settings.OllamaCachedModelsJson = freshJson;
                                changed = true;
                            }
                        }

                        if (changed)
                        {
                            _ = SettingsService.SaveAsync();
                            var currentAntigravity = LoadCachedAntigravityModels(settings);
                            var currentOllama = LoadCachedOllamaModels(settings);
                            RebuildAvailableModels(currentAntigravity, currentOllama, cloudLabel, localLabel, settings,
                                freshRecommendedLabels);
                        }
                    });
                }
                catch
                {
                }
            });
        }

        private static readonly string[] DefaultRecommendedOrder = new[]
        {
            "Gemini 3.7 Flash",
            "Gemini 3.6 Flash",
            "Gemini 3.5 Flash",
            "Gemini 3.1 Pro",
            "Claude Sonnet 4.6 (Thinking)",
            "Claude Opus 4.6 (Thinking)",
            "GPT-OSS 120B (Medium)"
        };

        private static readonly Dictionary<string, int> LevelOrderMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["High"] = 0,
            ["Medium"] = 1,
            ["Low"] = 2
        };

        private void RebuildAvailableModels(
            IReadOnlyList<AntigravityCascadeModel> antigravityModels,
            IReadOnlyList<string> ollamaModels,
            string cloudLabel,
            string localLabel,
            GlobalSettings settings,
            IReadOnlyList<string>? recommendedLabels = null)
        {
            AvailableModels.Clear();
            AvailableModelGroups.Clear();

            // Populate Antigravity
            if (settings.IsAntigravityLoggedIn && antigravityModels.Count > 0)
            {
                var antigravityGroup = AddProviderGroup("Antigravity");

                // Group models by candidate parent name
                var groupedCandidates =
                    new Dictionary<string, List<(AntigravityCascadeModel Model, string? Level)>>(StringComparer
                        .OrdinalIgnoreCase);

                foreach (var model in antigravityModels)
                {
                    var match = ModelLevelRegex.Match(model.Label);
                    if (match.Success)
                    {
                        string candidateParent = match.Groups[1].Value.Trim();
                        string level = match.Groups[2].Value.Trim();
                        if (!groupedCandidates.TryGetValue(candidateParent, out var list))
                        {
                            list = new List<(AntigravityCascadeModel, string?)>();
                            groupedCandidates[candidateParent] = list;
                        }

                        list.Add((model, level));
                    }
                    else
                    {
                        if (!groupedCandidates.TryGetValue(model.Label, out var list))
                        {
                            list = new List<(AntigravityCascadeModel, string?)>();
                            groupedCandidates[model.Label] = list;
                        }

                        list.Add((model, null));
                    }
                }

                // Build parent & standalone ModelOption items
                var builtOptions = new List<(ModelOption Option, int SortOrder)>();

                foreach (var (groupKey, items) in groupedCandidates)
                {
                    int sortOrder = GetSortOrderIndex(groupKey, recommendedLabels);

                    if (items.Count > 1)
                    {
                        // Submenu item (e.g. Gemini models with High, Medium, Low)
                        var firstModel = items[0].Model;
                        var parentOption = new ModelOption
                        {
                            Name = firstModel.ModelId,
                            DisplayName = groupKey,
                            MenuName = groupKey,
                            Provider = "Antigravity",
                            IsCloud = true,
                            ScopeLabel = cloudLabel,
                            SpeedBadge = firstModel.TagTitle ?? "",
                            ModelId = firstModel.ModelId,
                            ModelEnum = firstModel.ModelEnum
                        };

                        // Sort submodels: High -> Medium -> Low
                        var sortedItems = items.OrderBy(i =>
                            i.Level != null && LevelOrderMap.TryGetValue(i.Level, out int o) ? o : 99).ToList();
                        foreach (var (m, lvl) in sortedItems)
                        {
                            var subOption = new ModelOption
                            {
                                Name = m.ModelId,
                                DisplayName = $"{groupKey} ({lvl})",
                                MenuName = lvl ?? m.Label,
                                Provider = "Antigravity",
                                IsCloud = true,
                                ScopeLabel = cloudLabel,
                                SpeedBadge = m.TagTitle ?? "",
                                ModelId = m.ModelId,
                                ModelEnum = m.ModelEnum
                            };
                            parentOption.SubModels.Add(subOption);
                            AvailableModels.Add(subOption);
                        }

                        builtOptions.Add((parentOption, sortOrder));
                        AvailableModels.Add(parentOption);
                    }
                    else
                    {
                        // Standalone single item (e.g. Claude Sonnet, Claude Opus, GPT-OSS 120B (Medium))
                        var singleModel = items[0].Model;
                        var standaloneOption = new ModelOption
                        {
                            Name = singleModel.ModelId,
                            DisplayName = singleModel.Label,
                            MenuName = singleModel.Label,
                            Provider = "Antigravity",
                            IsCloud = true,
                            ScopeLabel = cloudLabel,
                            SpeedBadge = singleModel.TagTitle ?? "",
                            ModelId = singleModel.ModelId,
                            ModelEnum = singleModel.ModelEnum
                        };

                        builtOptions.Add((standaloneOption, sortOrder));
                        AvailableModels.Add(standaloneOption);
                    }
                }

                // Add to antigravityGroup in sorted order
                foreach (var (opt, _) in builtOptions.OrderBy(b => b.SortOrder))
                {
                    antigravityGroup.Models.Add(opt);
                }
            }

            // Populate Ollama
            if (ollamaModels.Count > 0)
            {
                var ollamaGroup = AddProviderGroup("Ollama");
                foreach (var oModel in ollamaModels)
                {
                    bool isCloud = oModel.Contains("cloud", StringComparison.OrdinalIgnoreCase);
                    AddModelOption(ollamaGroup, oModel, "Ollama", isCloud, isCloud ? cloudLabel : localLabel);
                }
            }

            RestoreSelectedModel(settings);
        }

        private static int GetSortOrderIndex(string groupKey, IReadOnlyList<string>? recommendedLabels)
        {
            if (recommendedLabels != null && recommendedLabels.Count > 0)
            {
                for (int i = 0; i < recommendedLabels.Count; i++)
                {
                    string rec = recommendedLabels[i];
                    if (rec.StartsWith(groupKey, StringComparison.OrdinalIgnoreCase) ||
                        groupKey.StartsWith(rec, StringComparison.OrdinalIgnoreCase) ||
                        rec.Contains(groupKey, StringComparison.OrdinalIgnoreCase) ||
                        groupKey.Contains(rec, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            for (int i = 0; i < DefaultRecommendedOrder.Length; i++)
            {
                string def = DefaultRecommendedOrder[i];
                if (def.StartsWith(groupKey, StringComparison.OrdinalIgnoreCase) ||
                    groupKey.StartsWith(def, StringComparison.OrdinalIgnoreCase) ||
                    def.Contains(groupKey, StringComparison.OrdinalIgnoreCase) ||
                    groupKey.Contains(def, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            // Fallback version-based ranking for unknown future models
            if (groupKey.Contains("3.8", StringComparison.OrdinalIgnoreCase)) return 5;
            if (groupKey.Contains("3.7", StringComparison.OrdinalIgnoreCase)) return 10;
            if (groupKey.Contains("3.6", StringComparison.OrdinalIgnoreCase)) return 20;
            if (groupKey.Contains("3.5", StringComparison.OrdinalIgnoreCase)) return 30;
            if (groupKey.Contains("3.1", StringComparison.OrdinalIgnoreCase)) return 40;
            if (groupKey.Contains("Claude", StringComparison.OrdinalIgnoreCase)) return 50;
            if (groupKey.Contains("GPT", StringComparison.OrdinalIgnoreCase)) return 60;
            return 100;
        }

        private void RestoreSelectedModel(GlobalSettings settings)
        {
            string provider = settings.LastSelectedProvider ?? settings.AiProvider ?? "Antigravity";
            string savedModel = string.IsNullOrWhiteSpace(settings.LastSelectedModel)
                ? (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
                    ? settings.OllamaModel
                    : settings.AntigravityModel)
                : settings.LastSelectedModel;

            if (provider.Equals("Antigravity", StringComparison.OrdinalIgnoreCase))
            {
                var matched = AvailableModels.FirstOrDefault(m =>
                    m.Provider.Equals("Antigravity", StringComparison.OrdinalIgnoreCase) &&
                    (m.Name.Equals(savedModel, StringComparison.OrdinalIgnoreCase) ||
                     m.ModelId.Equals(savedModel, StringComparison.OrdinalIgnoreCase) ||
                     m.DisplayName.Equals(savedModel, StringComparison.OrdinalIgnoreCase)));

                if (matched == null && !string.IsNullOrWhiteSpace(savedModel))
                {
                    string normalized = savedModel.Replace('_', '-').ToLowerInvariant();
                    matched = AvailableModels.FirstOrDefault(m =>
                        m.Provider.Equals("Antigravity", StringComparison.OrdinalIgnoreCase) &&
                        (m.ModelId.ToLowerInvariant().Contains(normalized) ||
                         normalized.Contains(m.ModelId.ToLowerInvariant())));
                }

                SelectedModel = matched ?? AvailableModels.FirstOrDefault(m =>
                        m.Provider.Equals("Antigravity", StringComparison.OrdinalIgnoreCase) &&
                        m.ModelId.Equals("gemini-3.7-flash-high", StringComparison.OrdinalIgnoreCase))
                    ?? AvailableModels.FirstOrDefault(m =>
                        m.Provider.Equals("Antigravity", StringComparison.OrdinalIgnoreCase));
            }
            else if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                var matched = AvailableModels.FirstOrDefault(m =>
                    m.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase) &&
                    m.Name.Equals(savedModel, StringComparison.OrdinalIgnoreCase));

                SelectedModel = matched ?? AvailableModels.FirstOrDefault(m =>
                    m.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase));
            }

            if (SelectedModel != null)
            {
                if (SelectedModel.Provider.Equals("Antigravity", StringComparison.OrdinalIgnoreCase))
                    settings.AntigravityModel = SelectedModel.ModelId;
                else if (SelectedModel.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
                    settings.OllamaModel = SelectedModel.Name;
            }
        }

        private static List<AntigravityCascadeModel> LoadCachedAntigravityModels(GlobalSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.AntigravityCachedModelsJson) &&
                settings.AntigravityCachedModelsJson != "[]")
            {
                try
                {
                    var cached =
                        JsonSerializer.Deserialize<List<AntigravityCascadeModel>>(settings.AntigravityCachedModelsJson);
                    if (cached != null && cached.Count > 0)
                        return cached;
                }
                catch
                {
                }
            }

            return GetDefaultAntigravityModels();
        }

        private static List<string> LoadCachedOllamaModels(GlobalSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.OllamaCachedModelsJson) &&
                settings.OllamaCachedModelsJson != "[]")
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<List<string>>(settings.OllamaCachedModelsJson);
                    if (cached != null && cached.Count > 0)
                        return cached;
                }
                catch
                {
                }
            }

            string fallback = string.IsNullOrWhiteSpace(settings.OllamaModel) ||
                              settings.OllamaModel.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
                ? "deepseek-v4-flash"
                : settings.OllamaModel;
            return new List<string> { fallback };
        }

        private static List<AntigravityCascadeModel> GetDefaultAntigravityModels()
        {
            return new List<AntigravityCascadeModel>
            {
                new("Gemini 3.7 Flash (High)", "gemini-3.7-flash-high", "MODEL_PLACEHOLDER_M298", null, true, true, 1.0,
                    null),
                new("Gemini 3.7 Flash (Medium)", "gemini-3.7-flash-medium", "MODEL_PLACEHOLDER_M299", "Fast", true,
                    true, 1.0, null),
                new("Gemini 3.7 Flash (Low)", "gemini-3.7-flash-low", "MODEL_PLACEHOLDER_M300", "Fast", true, true, 1.0,
                    null),
                new("Gemini 3.6 Flash (High)", "gemini-3.6-flash-high", "MODEL_PLACEHOLDER_M71", "Fast", true, true,
                    1.0, null),
                new("Gemini 3.6 Flash (Medium)", "gemini-3.6-flash-medium", "MODEL_PLACEHOLDER_M72", "Fast", true, true,
                    1.0, null),
                new("Gemini 3.6 Flash (Low)", "gemini-3.6-flash-low", "MODEL_PLACEHOLDER_M73", "Fast", true, true, 1.0,
                    null),
                new("Gemini 3.5 Flash (High)", "gemini-3-flash-agent", "MODEL_PLACEHOLDER_M84", "Fast", true, true, 1.0,
                    null),
                new("Gemini 3.5 Flash (Medium)", "gemini-3.5-flash-low", "MODEL_PLACEHOLDER_M20", "Fast", true, true,
                    1.0, null),
                new("Gemini 3.5 Flash (Low)", "gemini-3.5-flash-extra-low", "MODEL_PLACEHOLDER_M187", "Fast", true,
                    true, 1.0, null),
                new("Gemini 3.1 Pro (High)", "gemini-pro-agent", "MODEL_PLACEHOLDER_M16", null, true, true, 1.0, null),
                new("Gemini 3.1 Pro (Low)", "gemini-3.1-pro-low", "MODEL_PLACEHOLDER_M36", null, true, true, 1.0, null),
                new("Claude Sonnet 4.6 (Thinking)", "claude-sonnet-4-6", "MODEL_PLACEHOLDER_M35", null, true, true, 1.0,
                    null),
                new("Claude Opus 4.6 (Thinking)", "claude-opus-4-6-thinking", "MODEL_PLACEHOLDER_M26", null, true, true,
                    1.0, null),
                new("GPT-OSS 120B (Medium)", "gpt-oss-120b-medium", "MODEL_OPENAI_GPT_OSS_120B_MEDIUM", null, true,
                    true, 1.0, null),
            };
        }

        private void AddModelOption(ModelProviderGroup group, string name, string provider, bool isCloud,
            string scopeLabel, string? displayName = null)
        {
            var option = new ModelOption
            {
                Name = name,
                DisplayName = displayName ?? name,
                MenuName = displayName ?? name,
                Provider = provider,
                IsCloud = isCloud,
                ScopeLabel = scopeLabel
            };
            group.Models.Add(option);
            AvailableModels.Add(option);
        }

        private ModelProviderGroup AddProviderGroup(string provider)
        {
            var group = new ModelProviderGroup
            {
                Provider = provider
            };
            AvailableModelGroups.Add(group);
            return group;
        }

        private static string GetResource(Microsoft.Windows.ApplicationModel.Resources.ResourceLoader loader,
            string key, string fallback)
        {
            try
            {
                string value = loader.GetString(key);
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        public void RefreshGroupedSessions()
        {
            GroupedSessions.Clear();
            var filtered = _conversationSearch.Search(Sessions, SearchQuery);
            bool isFiltering = !string.IsNullOrWhiteSpace(SearchQuery);

            var pinned = filtered.Where(s => s.IsPinned).OrderByDescending(s => s.UpdatedAt).ToList();
            var unpinned = filtered.Where(s => !s.IsPinned).ToList();

            var rl = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            string generalText = "Conversations";
            try
            {
                generalText = rl.GetString("ChatGeneralText");
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(generalText)) generalText = "Conversations";

            string pinnedText = "Pinned Conversations";
            try
            {
                pinnedText = rl.GetString("ChatPinnedText");
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(pinnedText)) pinnedText = "Pinned Conversations";

            var groups = unpinned.GroupBy(s =>
                    string.IsNullOrWhiteSpace(s.ProjectName) || s.ProjectName == "General"
                        ? generalText
                        : s.ProjectName)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Keep empty registered projects visible in the normal history view,
            // but never manufacture an empty project/result while searching.
            foreach (var proj in isFiltering ? Enumerable.Empty<ProjectInfo>() : RegisteredProjects)
            {
                if (!groups.ContainsKey(proj.Name))
                {
                    groups[proj.Name] = new List<ChatSessionInfo>();
                }
            }

            if (pinned.Count > 0)
            {
                GroupedSessions.Add(new SessionGroup(pinnedText, pinned, isNested: false, isHeaderOnly: false));
            }

            var sortedGroups = groups.OrderBy(g => g.Key == generalText ? 1 : 0)
                .ThenBy(g => g.Key);

            var projects = sortedGroups.Where(g => g.Key != generalText).ToList();
            var generalGroup = sortedGroups.FirstOrDefault(g => g.Key == generalText);

            if (projects.Count > 0 || (!isFiltering && RegisteredProjects.Count > 0))
            {
                string projectsText = "Projects";
                try
                {
                    projectsText = rl.GetString("ChatProjectsText");
                }
                catch
                {
                }

                if (string.IsNullOrEmpty(projectsText)) projectsText = "Projects";

                GroupedSessions.Add(new SessionGroup(projectsText, new List<ChatSessionInfo>(), isNested: false,
                    isHeaderOnly: true));

                foreach (var g in projects)
                {
                    var sortedItems = g.Value.OrderByDescending(s => s.UpdatedAt).ToList();
                    if (sortedItems.Count == 0 && !isFiltering)
                    {
                        string noConvText = "No conversations";
                        try
                        {
                            noConvText = rl.GetString("ChatNoConversationsText");
                        }
                        catch
                        {
                        }

                        if (string.IsNullOrEmpty(noConvText)) noConvText = "No conversations";
                        sortedItems.Add(new ChatSessionInfo
                            { Title = noConvText, IsPlaceholder = true, SessionId = "placeholder_" + g.Key });
                    }

                    GroupedSessions.Add(new SessionGroup(g.Key, sortedItems, isNested: true, isHeaderOnly: false));
                }
            }

            if (generalGroup.Key != null)
            {
                var sortedItems = generalGroup.Value.OrderByDescending(s => s.UpdatedAt).ToList();
                if (sortedItems.Count == 0 && !isFiltering)
                {
                    string noConvText = "No conversations";
                    try
                    {
                        noConvText = rl.GetString("ChatNoConversationsText");
                    }
                    catch
                    {
                    }

                    if (string.IsNullOrEmpty(noConvText)) noConvText = "No conversations";
                    sortedItems.Add(new ChatSessionInfo
                        { Title = noConvText, IsPlaceholder = true, SessionId = "placeholder_" + generalGroup.Key });
                }

                GroupedSessions.Add(new SessionGroup(generalGroup.Key, sortedItems, isNested: false,
                    isHeaderOnly: false));
            }

            OnPropertyChanged(nameof(GroupedSessions));
        }

        public async Task AddProjectAsync(string path)
        {
            var proj = await _knowledgeStore.AddProjectAsync(path);
            if (proj != null && !RegisteredProjects.Any(p => p.Path == proj.Path))
            {
                RegisteredProjects.Add(proj);
                AvailableProjects.Add(proj.Name);
                RefreshGroupedSessions();
            }
        }

        [RelayCommand]
        public async Task NewSessionAsync()
        {
            var rl = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            string newConvTitle = "New Conversation";
            try
            {
                newConvTitle = rl.GetString("ChatNewConversationTitle");
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(newConvTitle)) newConvTitle = "New Conversation";

            var session = await _knowledgeStore.CreateChatSessionAsync(newConvTitle, "General");
            Sessions.Insert(0, session);
            RefreshGroupedSessions();
            SelectedSession = session;
        }

        [RelayCommand]
        public async Task NewProjectSessionAsync(string projectName)
        {
            var rl = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            string newConvTitle = "New Conversation";
            try
            {
                newConvTitle = rl.GetString("ChatNewConversationTitle");
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(newConvTitle)) newConvTitle = "New Conversation";

            string generalText = rl.GetString("ChatGeneralText");
            if (string.IsNullOrEmpty(generalText)) generalText = "Conversations";

            string actualProjectName = projectName;
            if (actualProjectName == "No Project" || actualProjectName == "Conversations" ||
                actualProjectName == generalText || actualProjectName == "Pinned Conversations")
            {
                actualProjectName = "General";
            }

            var session = await _knowledgeStore.CreateChatSessionAsync(newConvTitle, actualProjectName);
            Sessions.Insert(0, session);
            RefreshGroupedSessions();
            _selectedProjectForNewChat =
                (actualProjectName == "General" || string.IsNullOrWhiteSpace(actualProjectName))
                    ? _localizedGeneralText
                    : actualProjectName;
            OnPropertyChanged(nameof(SelectedProjectForNewChat));
            SelectedSession = session;
        }

        [RelayCommand]
        public async Task DeleteSessionAsync(ChatSessionInfo? session)
        {
            var target = session ?? SelectedSession;
            if (target == null) return;

            await _knowledgeStore.DeleteChatSessionAsync(target.SessionId);
            Sessions.Remove(target);
            RefreshGroupedSessions();

            if (SelectedSession == target)
            {
                SelectedSession = Sessions.FirstOrDefault();
                if (SelectedSession == null)
                {
                    await NewSessionAsync();
                }
            }
        }

        [RelayCommand]
        public void ToggleSelectionMode()
        {
            IsSelectionMode = !IsSelectionMode;
        }

        [RelayCommand]
        public async Task DeleteSelectedSessionsAsync(System.Collections.IList items)
        {
            if (items == null || items.Count == 0) return;
            var list = items.Cast<ChatSessionInfo>().ToList();
            foreach (var target in list)
            {
                await _knowledgeStore.DeleteChatSessionAsync(target.SessionId);
                Sessions.Remove(target);
            }

            RefreshGroupedSessions();
            IsSelectionMode = false;

            if (SelectedSession == null || !Sessions.Contains(SelectedSession))
            {
                SelectedSession = Sessions.FirstOrDefault();
                if (SelectedSession == null)
                {
                    await NewSessionAsync();
                }
            }
        }

        [RelayCommand]
        public async Task RenameSessionAsync(ChatSessionInfo? session)
        {
            var target = session ?? SelectedSession;
            if (target == null) return;

            await _knowledgeStore.UpdateChatSessionTitleAsync(target.SessionId, target.Title);
        }

        public void RenameSession(ChatSessionInfo session, string newTitle)
        {
            if (session == null || string.IsNullOrWhiteSpace(newTitle)) return;
            session.Title = newTitle;
            var idx = Sessions.IndexOf(session);
            if (idx >= 0)
            {
                // Force UI update
                Sessions[idx] = session;
            }

            _ = _knowledgeStore.UpdateChatSessionTitleAsync(session.SessionId, newTitle);
            RefreshGroupedSessions();
        }

        [RelayCommand]
        public async Task TogglePinSessionAsync(ChatSessionInfo? session)
        {
            var target = session ?? SelectedSession;
            if (target == null) return;

            target.IsPinned = !target.IsPinned;

            var idx = Sessions.IndexOf(target);
            if (idx >= 0)
            {
                Sessions[idx] = target;
            }

            await _knowledgeStore.ToggleChatSessionPinAsync(target.SessionId, target.IsPinned);
            RefreshGroupedSessions();
        }

        [RelayCommand]
        public async Task LoadChatHistoryAsync()
        {
            try
            {
                ChatHistory.Clear();
                if (SelectedSession == null || SelectedSession.IsPlaceholder)
                {
                    IsNewChat = false;
                    return;
                }

                string sid = SelectedSession.SessionId;
                var messages = await _knowledgeStore.GetChatMessagesBySessionAsync(sid, 100);
                foreach (var (role, content, _) in messages)
                {
                    ChatHistory.Add(new ChatMessage { Role = role, Content = content, IsThinking = false });
                }

                IsNewChat = ChatHistory.Count == 0;
                if (IsNewChat)
                {
                    string p = string.IsNullOrWhiteSpace(SelectedSession.ProjectName)
                        ? "General"
                        : SelectedSession.ProjectName;
                    _selectedProjectForNewChat = (p == "General") ? _localizedGeneralText : p;
                    OnPropertyChanged(nameof(SelectedProjectForNewChat));
                }
            }
            catch
            {
                // Ignore errors loading history
            }
        }

        [RelayCommand]
        private async Task ClearChatHistoryAsync()
        {
            await DeleteSessionAsync(SelectedSession);
        }

        [RelayCommand]
        private void CancelChat()
        {
            if (IsBusy && _chatCts != null)
            {
                _chatCts.Cancel();
            }
        }

        [RelayCommand]
        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(UserInput) || IsBusy)
            {
                return;
            }

            _chatCts?.Cancel();
            _chatCts?.Dispose();
            _chatCts = new CancellationTokenSource();
            var cancellationToken = _chatCts.Token;

            await InitializeAsync();

            if (SelectedSession == null)
            {
                string targetProject = CurrentWorkspace ?? "General";
                await NewProjectSessionAsync(targetProject);
            }

            string prompt = UserInput;
            UserInput = string.Empty;

            var rl = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            string newConvTitle = rl.GetString("ChatNewConversationTitle");
            if (string.IsNullOrEmpty(newConvTitle)) newConvTitle = "New Conversation";

            // Auto-generate title if it's currently a new conversation
            if (SelectedSession != null &&
                (SelectedSession.Title == "New Conversation" || SelectedSession.Title == newConvTitle))
            {
                string cleanTitle = prompt;
                if (prompt.StartsWith("/"))
                {
                    var parts = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        cleanTitle = $"{parts[0].Trim('/')}: {System.IO.Path.GetFileName(parts[1])}";
                    }
                }
                else if (prompt.Length > 28)
                {
                    cleanTitle = prompt.Substring(0, 25) + "...";
                }

                SelectedSession.Title = cleanTitle;
                await _knowledgeStore.UpdateChatSessionTitleAsync(SelectedSession.SessionId, cleanTitle);

                // Do not rebuild GroupedSessions or re-assign to avoid breaking UI selection while streaming.
            }

            string sessionId = SelectedSession?.SessionId ?? "default";

            // Dua message cua user vao ChatHistory truoc de UI hien thi ngay.
            var userMsg = new ChatMessage { Role = "User", Content = prompt };
            ChatHistory.Add(userMsg);
            IsNewChat = false;
            await _knowledgeStore.SaveChatMessageAsync(sessionId, "User", prompt);

            IsBusy = true;

            // Tao message placeholder cho AI/tool.
            var assistantMsg = new ChatMessage { Role = "AI", Content = "Dang xu ly...", IsThinking = true };
            ChatHistory.Add(assistantMsg);

            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            try
            {
                if (prompt.StartsWith("/md5 ", StringComparison.OrdinalIgnoreCase))
                {
                    assistantMsg.Role = "Tool";
                    assistantMsg.Content = await RunMd5ToolAsync(prompt.Substring(5).Trim());
                    assistantMsg.IsThinking = false;
                    return;
                }

                if (prompt.StartsWith("/route ", StringComparison.OrdinalIgnoreCase))
                {
                    assistantMsg.Role = "Router";
                    assistantMsg.Content = await RunRouteAsync(prompt.Substring(7).Trim());
                    assistantMsg.IsThinking = false;
                    return;
                }

                if (prompt.StartsWith("/triage ", StringComparison.OrdinalIgnoreCase))
                {
                    assistantMsg.Role = "Triage";
                    assistantMsg.Content = "Đang khởi tạo tiến trình Triage...\n";

                    string finalResult = await RunTriageToolAsync(
                        prompt.Substring(8).Trim(),
                        (status) =>
                        {
                            if (dispatcherQueue != null)
                            {
                                dispatcherQueue.TryEnqueue(() => { assistantMsg.Content += $"⏳ {status}\n"; });
                            }
                            else
                            {
                                assistantMsg.Content += $"⏳ {status}\n";
                            }
                        },
                        cancellationToken
                    );

                    assistantMsg.Content = finalResult;
                    assistantMsg.IsThinking = false;
                    return;
                }

                var settings = SettingsService.Current;
                string provider = settings?.AiProvider ?? "Antigravity";
                bool isFirstChunk = true;

                if (provider == "Antigravity")
                {
                    string modelName = SelectedModel?.ModelId ?? settings?.AntigravityModel ?? "flash";
                    string modelEnum = SelectedModel?.ModelEnum ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(modelEnum))
                    {
                        await AntigravityLanguageServerClient.SetModelStateAsync(modelEnum);
                    }

                    var antigravityClient = new AntigravityLanguageServerClient();

                    await foreach (string chunk in antigravityClient.GenerateStreamAsync(prompt, _systemPrompt,
                                       modelName, CurrentWorkspace ?? "General"))
                    {
                        if (isFirstChunk)
                        {
                            assistantMsg.Content = chunk;
                            assistantMsg.IsThinking = false;
                            isFirstChunk = false;
                        }
                        else
                        {
                            assistantMsg.Content += chunk;
                        }
                    }
                }
                else
                {
                    string port = settings?.OllamaPort ?? "11434";
                    string endpoint = $"http://localhost:{port}";
                    string modelName = settings?.OllamaModel ?? "deepseek-v4-flash";

                    await foreach (string chunk in _ollamaClient.GenerateStreamAsync(prompt, _systemPrompt, endpoint,
                                       modelName))
                    {
                        if (isFirstChunk)
                        {
                            assistantMsg.Content = chunk;
                            assistantMsg.IsThinking = false;
                            isFirstChunk = false;
                        }
                        else
                        {
                            assistantMsg.Content += chunk;
                        }
                    }
                }

                if (isFirstChunk)
                {
                    assistantMsg.IsThinking = false;
                }
            }
            catch (OperationCanceledException)
            {
                assistantMsg.IsThinking = false;
                assistantMsg.Content = "Đã hủy tác vụ.";
            }
            catch (Exception ex)
            {
                assistantMsg.IsThinking = false;
                assistantMsg.Content += $"\n[Error: {ex.Message}]";
            }
            finally
            {
                IsBusy = false;
                await _knowledgeStore.SaveChatMessageAsync(sessionId, assistantMsg.Role, assistantMsg.Content);
            }
        }

        // Hop den MD5:
        // Input: duong dan file tu chat command.
        // Output: text gon de hien thi lai trong khung chat.
        // Ben trong ham nay la luong MCP that: load registry -> spawn server -> tools/list -> tools/call.
        private async Task<string> RunMd5ToolAsync(string path)
        {
            path = path.Trim().Trim('"');
            if (!System.IO.File.Exists(path)) return "Error: File not found.";
            try
            {
                var result = await _mcpManager.CallToolAsync("static.md5", "md5_file", new { path });
                if (result.TryGetProperty("md5", out var md5))
                    return $"MD5: {md5.GetString()}\nPath: {path}";
            }
            catch (ToolPolicyDeniedException) { throw; }
            catch { /* Read-only native fallback when the optional MCP backend is unavailable. */ }
            var hashes = await HashCalculator.CalculateHashesAsync(path);
            string hash = hashes.TryGetPropertyValue("md5", out var value) ? value?.ToString() ?? "N/A" : "N/A";
            return $"MD5: {hash}\nSize: {new System.IO.FileInfo(path).Length} bytes\nPath: {path}";
        }

        // Hop den Static Triage (v2 — dùng TriageService thay cho TriageOrchestrator):
        // Input: duong dan file.
        // Output: Markdown Report cho UI + AI-friendly context cho prompt injection.
        private async Task<string> RunTriageToolAsync(string path, Action<string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            path = path.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
            {
                return "Usage: /triage D:\\path\\to\\file";
            }

            onProgress?.Invoke("Đang chạy Triage qua ITriagePipeline...");
            TriageResult result = await _triagePipeline.RunTriageAsync(path, cancellationToken);

            // Lưu AI-friendly context vào _lastTriageFacts cho prompt injection
            _lastTriageFacts = JsonSerializer.Serialize(
                result.ToAIFriendlyContext(),
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

            // Nếu có markdown report, hiển thị cho user
            if (!string.IsNullOrEmpty(result.MarkdownReport))
            {
                string mdText = result.MarkdownReport;
                try
                {
                    string appRoot = AgentConfigLoader.GetAppRootPath();
                    string reportsDir = System.IO.Path.Combine(appRoot, "Reports");
                    System.IO.Directory.CreateDirectory(reportsDir);

                    string fileName = result.FileName;
                    string md5 = result.MD5Hash != "N/A" ? result.MD5Hash : "unknown";

                    string outPath = System.IO.Path.Combine(reportsDir, $"{fileName}_{md5}.md");

                    // Triage luon la buoc dau tien, nen ta se viet moi/ghi de de reset lich su file.
                    string header =
                        $"# 🛡️ Hồ Sơ Phân Tích Toàn Diện: {fileName}\n> Khởi tạo: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n";
                    System.IO.File.WriteAllText(outPath, header + mdText, System.Text.Encoding.UTF8);

                    mdText += $"\n\n---\n📁 *Hồ sơ phân tích đã được tạo tại:* `{outPath}`";
                }
                catch (Exception ex)
                {
                    mdText += $"\n\n[Warning: Cannot save report file: {ex.Message}]";
                }

                return mdText;
            }

            // Fallback: hiển thị AI context nếu không có markdown report
            return _lastTriageFacts;
        }

        // Hop den Router:
        // Input: duong dan file tu chat command.
        // Output: triage facts + route duoc chon + ket qua tool MCP neu route da enabled.
        // Tu Block 3.2, router khong doan theo ext
        private static async Task<string> RunRouteAsync(string path)
        {
            return await Task.FromResult($"Routing temporarily disabled during refactoring for path: {path}");
        }

        private static string FormatToolResult(JsonElement result)
        {
            JsonElement structured = result.TryGetProperty("structuredContent", out JsonElement structuredContent)
                ? structuredContent
                : result;

            if (structured.TryGetProperty("path", out JsonElement resultPath) &&
                structured.TryGetProperty("md5", out JsonElement md5) &&
                structured.TryGetProperty("size", out JsonElement size))
            {
                return $"MD5: {md5.GetString()}\nSize: {size.GetInt64()} bytes\nPath: {resultPath.GetString()}";
            }

            return JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}