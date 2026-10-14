using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using RE.Core;
using RE.Core.Services;
using RE.Domain.Models;
using RE.Infrastructure.AI;
using RE.Presentation.ViewModels;
using RE.WinUI.Core.Settings;
using RE.WinUI.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace RE.WinUI.Views
{
    public sealed partial class ChatPage : Page
    {
        public ChatViewModel ViewModel { get; }
        private double _sessionUsage;
        private double _weeklyUsage;
        private DateTimeOffset? _sessionReset;
        private DateTimeOffset? _weeklyReset;
        private bool _lastAntigravityAuthenticated;
        private AntigravityUsageSnapshot? _usageSnapshot;
        private bool _needsRebuildModelPicker = false;

        public ChatPage()
        {
            // Noi UI voi ViewModel:
            // XAML binding doc ChatHistory/UserInput va goi SendMessageCommand tu object nay.
            ViewModel = (Microsoft.UI.Xaml.Application.Current as App)!.Services.GetRequiredService<ChatViewModel>();
            DataContext = ViewModel;
            _lastAntigravityAuthenticated = SettingsService.Current.IsAntigravityLoggedIn;

            InitializeComponent();
            Bindings.Update();

            // Nap prompt config ngay khi mo page.
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.AvailableModelGroups.CollectionChanged +=
                (s, e) => DispatcherQueue.TryEnqueue(BuildModelPickerMenu);
            if (ModelPickerFlyout != null)
            {
                ModelPickerFlyout.Closed += (s, e) =>
                {
                    if (_needsRebuildModelPicker)
                    {
                        _needsRebuildModelPicker = false;
                        BuildModelPickerMenu();
                    }
                };
            }

            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            _ = InitializeViewModelAsync();

            // Auto-scroll khi co tin nhan moi
            ViewModel.ChatHistory.CollectionChanged += ChatHistory_CollectionChanged;
            Loaded += (s, e) => UpdateLanguage();
        }

        private async Task InitializeViewModelAsync()
        {
            await ViewModel.InitializeAsync();
            DispatcherQueue.TryEnqueue(() =>
            {
                GroupedSessionsSource.Source = null;
                GroupedSessionsSource.Source = ViewModel.GroupedSessions;
            });
            UpdateUsageButtonState();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.SelectedModel))
            {
                UpdateUsageButtonState();
            }
            else if (e.PropertyName == nameof(ViewModel.GroupedSessions))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    GroupedSessionsSource.Source = null;
                    GroupedSessionsSource.Source = ViewModel.GroupedSessions;
                });
            }
            else if (e.PropertyName == nameof(ViewModel.SelectedProjectForNewChat) ||
                     e.PropertyName == nameof(ViewModel.IsSelectionMode))
            {
                DispatcherQueue.TryEnqueue(UpdateLanguage);
            }
        }

        public static Visibility GetVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;

        public static Visibility GetInverseVisibility(bool isHidden) =>
            isHidden ? Visibility.Collapsed : Visibility.Visible;

        private void SettingsService_SettingsChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(UpdateUsageButtonState);
            bool authenticated = SettingsService.Current.IsAntigravityLoggedIn;
            if (authenticated != _lastAntigravityAuthenticated)
            {
                _lastAntigravityAuthenticated = authenticated;
                _ = ViewModel.LoadModelsAsync();
            }
        }

        private void UpdateUsageButtonState()
        {
            bool isOllama = ViewModel.SelectedModel?.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase) ==
                            true;
            bool antigravityAuthenticated = SettingsService.Current.IsAntigravityLoggedIn;
            ViewUsageButton.Visibility = isOllama || antigravityAuthenticated
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (ViewModel.SelectedModel != null && SelectedModelText != null)
            {
                SelectedModelText.Text = ViewModel.SelectedModel.DisplayName;
            }
        }

        private void ModelPickerItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: ModelOption model })
            {
                ViewModel.SelectedModel = model;
            }
        }

        public void UpdateLanguage()
        {
            var rl = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            try
            {
                if (ChatNewConversationText != null)
                    ChatNewConversationText.Text = rl.GetString("ChatNewConversation/Text");
                if (ChatSearchBox != null)
                    ChatSearchBox.PlaceholderText = rl.GetString("ChatSearchHistory/PlaceholderText");
                if (ChatProjectsHeaderText != null)
                    ChatProjectsHeaderText.Text = rl.GetString("ChatProjectsHeader/Text");
                if (ChatDeleteSelectedText != null)
                    ChatDeleteSelectedText.Text = rl.GetString("ChatDeleteSelected/Text");
                if (ChatInputBox != null) ChatInputBox.PlaceholderText = rl.GetString("ChatInputBox/PlaceholderText");
                if (ChatSendButton != null) ChatSendButton.Content = rl.GetString("ChatSendButton/Content");
                if (ViewUsageButtonText != null) ViewUsageButtonText.Text = GetResource(rl, "UsageButtonText", "Usage");
                if (ViewUsageButton != null)
                    ToolTipService.SetToolTip(ViewUsageButton, GetResource(rl, "ViewUsageToolTip", "View usage"));
                if (ChatUsageTitleText != null)
                    ChatUsageTitleText.Text = GetResource(rl, "UsageDialogTitle.Text", "Cloud usage");
                if (ChatRefreshUsageButton != null)
                    ChatRefreshUsageButton.Content = GetResource(rl, "UsageRefreshButton.Content", "Refresh");
                if (NotifyLimitsText != null)
                    NotifyLimitsText.Text = GetResource(rl, "NotifyLimitsCheck.Content",
                        "Notify me when I'm close to hitting my usage limits");
                if (ChatSelectConversationsBtn != null)
                    ToolTipService.SetToolTip(ChatSelectConversationsBtn,
                        rl.GetString("ChatSelectConversations/ToolTipService.ToolTip"));
                if (ChatFilterButton != null)
                    ToolTipService.SetToolTip(ChatFilterButton,
                        rl.GetString("ChatFilterButton/ToolTipService.ToolTip"));
                if (ChatAddProjectButton != null)
                    ToolTipService.SetToolTip(ChatAddProjectButton,
                        rl.GetString("ChatAddProjectButton/ToolTipService.ToolTip"));
                if (ChatToggleSidebarBtn != null)
                    ToolTipService.SetToolTip(ChatToggleSidebarBtn,
                        rl.GetString("ChatToggleSidebar/ToolTipService.ToolTip"));

                // Refresh model metadata so provider/cloud labels follow the selected language.
                _ = ViewModel.LoadModelsAsync();
            }
            catch
            {
            }
        }

        private static string GetResource(Microsoft.Windows.ApplicationModel.Resources.ResourceLoader loader,
            string key, string fallback)
        {
            try
            {
                var value = loader.GetString(key);
                if (string.IsNullOrWhiteSpace(value) && key.Contains('.'))
                {
                    value = loader.GetString(key.Replace('.', '/'));
                }
                else if (string.IsNullOrWhiteSpace(value) && key.Contains('/'))
                {
                    value = loader.GetString(key.Replace('/', '.'));
                }

                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                try
                {
                    if (key.Contains('.'))
                    {
                        var val = loader.GetString(key.Replace('.', '/'));
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                    else if (key.Contains('/'))
                    {
                        var val = loader.GetString(key.Replace('/', '.'));
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }
                catch
                {
                }

                return fallback;
            }
        }

        private void SetUsageText(double session, double weekly, DateTimeOffset? sessionReset = null,
            DateTimeOffset? weeklyReset = null)
        {
            _sessionUsage = session;
            _weeklyUsage = weekly;
            _sessionReset = sessionReset;
            _weeklyReset = weeklyReset;
        }

        private static string FormatResetText(
            Microsoft.Windows.ApplicationModel.Resources.ResourceLoader loader,
            DateTimeOffset? reset,
            string fallbackKey,
            string fallback)
        {
            if (reset.HasValue)
            {
                string format = GetResource(loader, "UsageResetAt", "Resets at {0}");
                return string.Format(format, reset.Value.ToLocalTime().ToString("g"));
            }

            return GetResource(loader, fallbackKey, fallback);
        }

        private void ChatHistory_CollectionChanged(object? sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Scroll to bottom whenever a new message is added
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ChatScrollViewer.UpdateLayout();
                    ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
                });

                foreach (var item in e.NewItems)
                {
                    if (item is ChatMessage message)
                    {
                        message.PropertyChanged -= Message_PropertyChanged;
                        message.PropertyChanged += Message_PropertyChanged;
                    }
                }
            }
        }

        private void Message_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatMessage.Content))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ChatScrollViewer.UpdateLayout();
                    ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
                });
            }
        }

        private void ToggleSidebarButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ChatSplitView.IsPaneOpen = !ChatSplitView.IsPaneOpen;
        }

        private void ViewUsageButton_Click(object sender, RoutedEventArgs e)
        {
            RenderUsageGroups();
            _ = RefreshUsageAsync();
        }

        private void UsageFlyout_Opened(object sender, object e)
        {
            RenderUsageGroups();
            _ = RefreshUsageAsync();
        }

        private void RefreshUsageFlyoutButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _ = RefreshUsageAsync();
        }

        private async Task RefreshUsageAsync()
        {
            try
            {
                if (_usageSnapshot == null && UsageLoadingRing != null)
                {
                    UsageLoadingRing.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    if (UsageItemsControl != null)
                        UsageItemsControl.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                }

                var antigravityClient = new AntigravityLanguageServerClient();
                _usageSnapshot = await antigravityClient.GetUsageAsync();
                RenderUsageGroups();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Quota refresh failed: {ex.Message}");
                RenderUsageGroups();
            }
            finally
            {
                if (UsageLoadingRing != null) UsageLoadingRing.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                if (UsageItemsControl != null) UsageItemsControl.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            }
        }

        private void RenderUsageGroups()
        {
            ViewModel.UsageGroups.Clear();
            var loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();

            // 1. Always render Antigravity quota groups
            if (_usageSnapshot != null && _usageSnapshot.Groups.Count > 0)
            {
                foreach (var group in _usageSnapshot.Groups)
                {
                    var displayGroup = new UsageGroupDisplayModel
                    {
                        DisplayName = LocalizeUsageGroup(group.DisplayName)
                    };

                    foreach (var bucket in group.Buckets.OrderBy(bucket =>
                                 bucket.Window.Equals("5h", StringComparison.OrdinalIgnoreCase) ? 0 :
                                 bucket.Window.Equals("weekly", StringComparison.OrdinalIgnoreCase) ? 1 : 2))
                    {
                        displayGroup.Buckets.Add(new UsageBucketDisplayModel
                        {
                            DisplayName = LocalizeUsageBucket(bucket),
                            RemainingPercent = bucket.RemainingPercent,
                            RemainingPercentText =
                                string.Format(GetResource(loader, "UsageRemainingFormat", "{0}% remaining"),
                                    bucket.RemainingPercent.ToString("0")),
                            HasResetTime = bucket.ResetTime.HasValue,
                            ResetTimeText = bucket.ResetTime.HasValue
                                ? string.Format(GetResource(loader, "UsageResetAt", "Resets at {0}"),
                                    bucket.ResetTime.Value.ToLocalTime().ToString("g"))
                                : "",
                            IsInformationOnly = false
                        });
                    }

                    ViewModel.UsageGroups.Add(displayGroup);
                }
            }
        }

        private string LocalizeUsageGroup(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return GetResource(new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader(),
                    "UsageGeminiModels", "Gemini Models");
            if (name.Contains("Claude", StringComparison.OrdinalIgnoreCase) &&
                name.Contains("GPT", StringComparison.OrdinalIgnoreCase))
                return GetResource(new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader(),
                    "UsageClaudeGptModels", "Claude and GPT models");
            if (name.Contains("Gemini", StringComparison.OrdinalIgnoreCase))
                return GetResource(new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader(),
                    "UsageGeminiModels", "Gemini Models");
            return name;
        }

        private string LocalizeUsageBucket(AntigravityUsageBucket bucket)
        {
            var loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            if (bucket.Window.Equals("weekly", StringComparison.OrdinalIgnoreCase))
                return GetResource(loader, "UsageWeeklyLimitRemaining", "Weekly Limit Remaining");
            if (bucket.Window.Equals("5h", StringComparison.OrdinalIgnoreCase))
                return GetResource(loader, "UsageFiveHourLimitRemaining", "Five Hour Limit Remaining");
            return bucket.DisplayName;
        }

        public void BuildModelPickerMenu()
        {
            if (ModelPickerFlyout == null) return;
            if (ModelPickerFlyout.IsOpen)
            {
                _needsRebuildModelPicker = true;
                return;
            }

            ModelPickerFlyout.Items.Clear();
            foreach (var group in ViewModel.AvailableModelGroups)
            {
                ModelPickerFlyout.Items.Add(new MenuFlyoutItem
                {
                    Text = group.Provider,
                    IsEnabled = false,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
                foreach (var model in group.Models)
                {
                    if (model.SubModels.Count > 0)
                    {
                        var subItem = new MenuFlyoutSubItem
                        {
                            Text = !string.IsNullOrEmpty(model.MenuName) ? model.MenuName : model.DisplayName,
                            Icon = model.IsCloud ? CreateCloudIcon() : null
                        };
                        foreach (var submodel in model.SubModels)
                        {
                            var radioItem = new RadioMenuFlyoutItem
                            {
                                Text = !string.IsNullOrEmpty(submodel.MenuName)
                                    ? submodel.MenuName
                                    : submodel.DisplayName,
                                Tag = submodel,
                                GroupName = model.DisplayName,
                                IsChecked = (ViewModel.SelectedModel?.DisplayName == submodel.DisplayName)
                            };
                            radioItem.Click += ModelPickerItem_Click;
                            subItem.Items.Add(radioItem);
                        }

                        ModelPickerFlyout.Items.Add(subItem);
                    }
                    else
                    {
                        var item = CreateModelMenuItem(model, true);
                        item.Click += ModelPickerItem_Click;
                        ModelPickerFlyout.Items.Add(item);
                    }
                }
            }
        }

        private static MenuFlyoutItem CreateModelMenuItem(ModelOption model, bool showCloudIcon)
        {
            var item = new MenuFlyoutItem
            {
                Text = !string.IsNullOrEmpty(model.MenuName) ? model.MenuName : model.DisplayName,
                Tag = model
            };
            if (showCloudIcon && model.IsCloud)
                item.Icon = CreateCloudIcon();
            return item;
        }

        private static FontIcon CreateCloudIcon()
        {
            return new FontIcon
            {
                Glyph = "\uE753",
                FontSize = 14,
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        private void UserInputTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                if (ViewModel.SendMessageCommand.CanExecute(null))
                {
                    ViewModel.SendMessageCommand.Execute(null);
                }

                e.Handled = true;
            }
        }

        private void CopyMessageButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string content)
            {
                var package = new DataPackage();
                package.SetText(content);
                Clipboard.SetContent(package);
                _ = ShowCopyToastAsync();
            }
        }

        private async Task ShowCopyToastAsync()
        {
            CopyToast.Opacity = 1;

            await Task.Delay(TimeSpan.FromMilliseconds(900));

            var animation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(220),
                EnableDependentAnimation = true
            };

            Storyboard.SetTarget(animation, CopyToast);
            Storyboard.SetTargetProperty(animation, "Opacity");

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        private async void RenameSession_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is RE.Domain.Models.ChatSessionInfo session)
            {
                var textBox = new TextBox { Text = session.Title, MinWidth = 300 };
                var dialog = new ContentDialog
                {
                    Title = "Rename Conversation",
                    Content = textBox,
                    PrimaryButtonText = "Save",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    if (!string.IsNullOrWhiteSpace(textBox.Text))
                    {
                        ViewModel.RenameSession(session, textBox.Text);
                    }
                }
            }
        }

        private async void DeleteSession_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ChatSessionInfo session)
            {
                string title = GetSafeLocalizedString("DeleteSessionDialogTitle", "Confirm Delete");
                string contentFormat = GetSafeLocalizedString("DeleteSessionDialogContent",
                    "Are you sure you want to delete the conversation '{0}'?");
                string primaryBtn = GetSafeLocalizedString("DeleteButton", "Delete");
                string closeBtn = GetSafeLocalizedString("CancelButton", "Cancel");

                var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    Title = title,
                    Content = string.Format(contentFormat, session.Title),
                    PrimaryButtonText = primaryBtn,
                    CloseButtonText = closeBtn,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                {
                    if (ViewModel.DeleteSessionCommand.CanExecute(session))
                    {
                        ViewModel.DeleteSessionCommand.Execute(session);
                    }
                }
            }
        }

        private void ChatSessionItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                if (grid.FindName("ActionPanel") is UIElement panel) panel.Opacity = 1;
                if (grid.FindName("TimeText") is UIElement timeText) timeText.Opacity = 0;
            }
        }

        private void ChatSessionItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                if (grid.FindName("ActionPanel") is UIElement panel) panel.Opacity = 0;
                if (grid.FindName("TimeText") is UIElement timeText) timeText.Opacity = 1;
            }
        }

        public static Microsoft.UI.Xaml.Visibility GetWelcomeVisibility(bool isNewChat)
        {
            return isNewChat ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        public static Microsoft.UI.Xaml.Visibility GetChatVisibility(bool isNewChat)
        {
            return isNewChat ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        }

        public static Microsoft.UI.Xaml.Visibility GetPinVisibility(bool isVisible)
        {
            return isVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        private static string GetSafeLocalizedString(string key, string defaultValue)
        {
            try
            {
                var rl = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
                string val = rl.GetString(key);
                return string.IsNullOrEmpty(val) ? defaultValue : val;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static string GetPinText(bool isPinned)
        {
            string pin = GetSafeLocalizedString("ChatPinText", "Pin");
            string unpin = GetSafeLocalizedString("ChatUnpinText", "Unpin");
            return isPinned ? unpin : pin;
        }

        public static string FormatRelativeTime(string relativeTime)
        {
            if (relativeTime == "just_now")
            {
                return GetSafeLocalizedString("TimeJustNow", "just now");
            }

            return relativeTime;
        }

        public static ListViewSelectionMode GetSelectionMode(bool isSelectionMode)
        {
            return isSelectionMode ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single;
        }

        public static Microsoft.UI.Xaml.Visibility GetProjectSubtitleVisibility(bool isPinned, string projectName)
        {
            if (!isPinned) return Microsoft.UI.Xaml.Visibility.Collapsed;
            string generalText = GetSafeLocalizedString("ChatGeneralText", "Conversations");
            if (string.IsNullOrWhiteSpace(projectName) || projectName == "General" || projectName == generalText)
                return Microsoft.UI.Xaml.Visibility.Collapsed;
            return Microsoft.UI.Xaml.Visibility.Visible;
        }

        public static string GetProjectNameDisplay(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName) || projectName == "General")
            {
                return GetSafeLocalizedString("ChatGeneralText", "Conversations");
            }

            return projectName;
        }

        public static string GetPinGlyph(bool isPinned)
        {
            return isPinned ? "\uE77A" : "\uE840";
        }

        public static Microsoft.UI.Xaml.Visibility GetNormalVisibility(bool isPlaceholder)
        {
            return isPlaceholder ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        }

        public static Microsoft.UI.Xaml.Visibility GetPlaceholderVisibility(bool isPlaceholder)
        {
            return isPlaceholder ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        public static Microsoft.UI.Xaml.Thickness GetHeaderMargin(bool isNested)
        {
            return isNested
                ? new Microsoft.UI.Xaml.Thickness(0, 12, 12, 8)
                : new Microsoft.UI.Xaml.Thickness(0, 16, 12, 8);
        }

        public static double GetHeaderFontSize(bool isNested)
        {
            return isNested ? 13 : 14;
        }

        public static Windows.UI.Text.FontWeight GetHeaderFontWeight(bool isNested)
        {
            return isNested ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold;
        }

        public static string GetGroupIcon(string key, bool isNested)
        {
            string pinnedText = GetSafeLocalizedString("ChatPinnedText", "Pinned Conversations");
            if (key == "Pinned Conversations" || key == pinnedText) return "\uE840";

            string generalText = GetSafeLocalizedString("ChatGeneralText", "Conversations");
            if (key == "No Project" || key == generalText || key == "Conversations") return "\uE81C";

            string projectsText = GetSafeLocalizedString("ChatProjectsText", "Projects");
            if (key == "Projects" || key == projectsText) return "\uE8F1";

            return "\uE8B7";
        }

        public static Microsoft.UI.Xaml.Visibility GetAddButtonVisibility(string key)
        {
            string projectsText = GetSafeLocalizedString("ChatProjectsText", "Projects");
            string pinnedText = GetSafeLocalizedString("ChatPinnedText", "Pinned Conversations");

            return (key == "Projects" || key == projectsText || key == "Pinned Conversations" || key == pinnedText)
                ? Microsoft.UI.Xaml.Visibility.Collapsed
                : Microsoft.UI.Xaml.Visibility.Visible;
        }

        private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ChatListView.SelectedItems.Count == 0) return;

            string title = GetSafeLocalizedString("DeleteSessionsDialogTitle", "Delete Selected Conversations");
            string contentFormat = GetSafeLocalizedString("DeleteSessionsDialogContent",
                "Are you sure you want to delete {0} selected conversations? This action cannot be undone.");
            string primaryBtn = GetSafeLocalizedString("DeleteAllButton", "Delete All");
            string closeBtn = GetSafeLocalizedString("CancelButton", "Cancel");

            var dialog = new ContentDialog
            {
                Title = title,
                Content = string.Format(contentFormat, ChatListView.SelectedItems.Count),
                PrimaryButtonText = primaryBtn,
                CloseButtonText = closeBtn,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteSelectedSessionsCommand.ExecuteAsync(ChatListView.SelectedItems);
            }
        }

        private async void AddProject_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(((App)Microsoft.UI.Xaml.Application.Current)
                .MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                await ViewModel.AddProjectAsync(folder.Path);
            }
        }

        private void ChatListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is ListViewItem lvi)
            {
                if (args.Item is ChatSessionInfo session && session.IsPlaceholder)
                {
                    lvi.IsEnabled = false;
                }
                else
                {
                    lvi.IsEnabled = true;
                }
            }
        }

        private void ChatListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel.IsSelectionMode) return;

            var session = e.AddedItems.OfType<ChatSessionInfo>().FirstOrDefault();
            if (session == null || session.IsPlaceholder) return;

            ViewModel.SelectedSession = session;
            ViewModel.IsNewChat = false;
        }

        private void ChatListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (ViewModel.IsSelectionMode) return;
            if (e.ClickedItem is not ChatSessionInfo session || session.IsPlaceholder) return;

            ChatListView.SelectedItem = session;
            ViewModel.SelectedSession = session;
            ViewModel.IsNewChat = false;
        }

        private void SidebarBackground_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (e.OriginalSource == sender)
            {
                ChatListView.SelectedItem = null;
                ViewModel.SelectedSession = null;
                ViewModel.ChatHistory.Clear();
                ViewModel.IsNewChat = false;
            }
        }
    }
}