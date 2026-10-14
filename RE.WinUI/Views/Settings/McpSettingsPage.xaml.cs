using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RE.Core.Mcp;
using RE.Core.Services;
using XamlApp = Microsoft.UI.Xaml.Application;

namespace RE.WinUI.Views.Settings
{
    public sealed partial class McpSettingsPage : Page
    {
        private McpClientManager? _mcpManager;

        private readonly Dictionary<string, (TextBlock statusText, ProgressRing progressRing, Button testButton)>
            _toolControls = new();

        public McpSettingsPage()
        {
            InitializeComponent();
            Loaded += McpSettingsPage_Loaded;
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double newWidth = Math.Min(800, e.NewSize.Width);
            if (MainStackPanel != null) MainStackPanel.Width = newWidth;
        }

        private async void McpSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (XamlApp.Current is App app)
            {
                _mcpManager = app.Services.GetService<McpClientManager>();
            }

            UpdateLanguage();
            await LoadToolsAsync();
        }

        public void UpdateLanguage()
        {
            DetailTitle.Text = GetLocalizedString("McpSettingsTitle.Text", "MCP Connections");
            DetailDescription.Text = GetLocalizedString("McpSettingsDesc.Text",
                "Manage and verify Model Context Protocol (MCP) server integrations.");
            RefreshAllButton.Content = GetLocalizedString("McpTestAllButton.Content", "Test All Connections");
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
                }
                catch
                {
                }

                return fallback;
            }
        }

        private async Task LoadToolsAsync()
        {
            if (_mcpManager == null) return;

            ToolsListStackPanel.Children.Clear();
            _toolControls.Clear();

            try
            {
                var registry = await _mcpManager.GetRegistryAsync();
                foreach (var tool in registry.Tools)
                {
                    var expander = CreateToolCard(tool);
                    ToolsListStackPanel.Children.Add(expander);
                }
            }
            catch (Exception ex)
            {
                var errorText = new TextBlock
                {
                    Text = $"Error loading MCP tools: {ex.Message}",
                    Foreground = new SolidColorBrush(Colors.Red)
                };
                ToolsListStackPanel.Children.Add(errorText);
            }
        }

        private SettingsExpander CreateToolCard(McpToolConfig tool)
        {
            var expander = new SettingsExpander
            {
                Header = tool.Name,
                Description = $"Transport: {tool.Transport.ToUpper()} | Cluster: {tool.Cluster}"
            };

            // Icon
            var icon = new FontIcon();
            if (tool.Transport.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                icon.Glyph = "\uE774"; // Globe/Network
            }
            else
            {
                icon.Glyph = "\uE756"; // Terminal/CLI
            }

            expander.HeaderIcon = icon;

            // Status Indicator Header Content
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var progress = new ProgressRing
                { Width = 16, Height = 16, IsActive = false, Visibility = Visibility.Collapsed };
            var statusText = new TextBlock
            {
                Text = GetLocalizedString("McpStatusUntested", "Untested"),
                Foreground = (Brush)XamlApp.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            var testBtn = new Button
            {
                Content = GetLocalizedString("McpTestButton", "Test"),
                Tag = tool.Id
            };
            testBtn.Click += async (s, e) => await TestSingleToolAsync(tool.Id);

            headerStack.Children.Add(progress);
            headerStack.Children.Add(statusText);
            headerStack.Children.Add(testBtn);

            expander.Header = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = tool.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock
                    {
                        Text = $"ID: {tool.Id} • Transport: {tool.Transport.ToUpper()}",
                        Foreground = (Brush)XamlApp.Current.Resources["TextFillColorSecondaryBrush"], FontSize = 12
                    }
                }
            };
            expander.Content = headerStack;

            // Expandable Items
            if (!string.IsNullOrEmpty(tool.Endpoint))
            {
                var endpointCard = new SettingsCard
                {
                    Header = GetLocalizedString("McpEndpointUrl", "Endpoint URL"),
                    Description = tool.Endpoint
                };
                expander.Items.Add(endpointCard);
            }

            if (!string.IsNullOrEmpty(tool.Command))
            {
                var cmdCard = new SettingsCard
                {
                    Header = GetLocalizedString("McpCommand", "Command"),
                    Description = $"{tool.Command} {string.Join(" ", tool.Args)}"
                };
                expander.Items.Add(cmdCard);
            }

            string timeoutFormat = GetLocalizedString("McpTimeoutFormat", "{0} seconds");
            var timeoutCard = new SettingsCard
            {
                Header = GetLocalizedString("McpTimeout", "Timeout"),
                Description = string.Format(timeoutFormat, tool.TimeoutSeconds)
            };
            expander.Items.Add(timeoutCard);

            _toolControls[tool.Id] = (statusText, progress, testBtn);
            return expander;
        }

        private async Task TestSingleToolAsync(string toolId)
        {
            if (_mcpManager == null || !_toolControls.TryGetValue(toolId, out var controls)) return;

            controls.testButton.IsEnabled = false;
            controls.progressRing.Visibility = Visibility.Visible;
            controls.progressRing.IsActive = true;
            controls.statusText.Text = GetLocalizedString("McpStatusConnecting", "Connecting...");
            controls.statusText.Foreground = (Brush)XamlApp.Current.Resources["TextFillColorSecondaryBrush"];

            try
            {
                var status = await _mcpManager.CheckStatusAsync(toolId);
                if (status.IsConnected)
                {
                    string format = GetLocalizedString("McpStatusConnected", "🟢 Connected ({0} tools)");
                    controls.statusText.Text = string.Format(format, status.ToolCount);
                    controls.statusText.Foreground = new SolidColorBrush(Colors.ForestGreen);
                }
                else
                {
                    string format = GetLocalizedString("McpStatusDisconnected", "🔴 Disconnected: {0}");
                    controls.statusText.Text = string.Format(format, status.Error);
                    controls.statusText.Foreground = new SolidColorBrush(Colors.IndianRed);
                }
            }
            catch (Exception ex)
            {
                string format = GetLocalizedString("McpStatusError", "🔴 Error: {0}");
                controls.statusText.Text = string.Format(format, ex.Message);
                controls.statusText.Foreground = new SolidColorBrush(Colors.IndianRed);
            }
            finally
            {
                controls.progressRing.IsActive = false;
                controls.progressRing.Visibility = Visibility.Collapsed;
                controls.testButton.IsEnabled = true;
            }
        }

        private async void RefreshAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mcpManager == null) return;

            RefreshAllButton.IsEnabled = false;
            GlobalProgressRing.Visibility = Visibility.Visible;
            GlobalProgressRing.IsActive = true;

            try
            {
                var tasks = new List<Task>();
                foreach (var toolId in _toolControls.Keys)
                {
                    tasks.Add(TestSingleToolAsync(toolId));
                }

                await Task.WhenAll(tasks);
            }
            finally
            {
                GlobalProgressRing.IsActive = false;
                GlobalProgressRing.Visibility = Visibility.Collapsed;
                RefreshAllButton.IsEnabled = true;
            }
        }
    }
}