using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.Core;
using revit_mcp_plugin.Helpers;

namespace revit_mcp_plugin.UI
{
    public partial class MCPDockablePanel : Page
    {
        private static MCPDockablePanel _instance;
        private readonly ObservableCollection<ChatMessage> _messages = new ObservableCollection<ChatMessage>();
        private readonly ObservableCollection<PromptChip> _chips = new ObservableCollection<PromptChip>();
        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _chipsTimer;
        private ClaudeRevitClient _client;
        private bool _isProcessing;
        private bool _lastStatus;

        private static readonly SolidColorBrush BrushOnline = new SolidColorBrush(Color.FromRgb(76, 175, 80));
        private static readonly SolidColorBrush BrushOffline = new SolidColorBrush(Color.FromRgb(244, 67, 54));
        private static readonly SolidColorBrush BrushOfflineText = new SolidColorBrush(Color.FromRgb(136, 136, 136));

        public static MCPDockablePanel Instance => _instance;

        public MCPDockablePanel()
        {
            InitializeComponent();
            _instance = this;
            ChatMessages.ItemsSource = _messages;

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _statusTimer.Tick += (s, e) => UpdateStatus();

            PromptChips.ItemsSource = _chips;
            _chipsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _chipsTimer.Tick += (s, e) => UpdateChips();

            ChatInput.TextChanged += (s, e) =>
            {
                Placeholder.Visibility = string.IsNullOrEmpty(ChatInput.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
            };

            AddMessage("assistant",
                "Hi! I'm Claude, your assistant for Revit.\n\n" +
                "I have direct access to the open model and can perform operations in real time. " +
                "Ask me anything about the project or tell me what to create.");
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _statusTimer.Start();
            _chipsTimer.Start();
            UpdateStatus();
            UpdateChips();
            ChatInput.Focus();
        }

        private void UpdateStatus()
        {
            try
            {
                bool running = Core.SocketService.Instance.IsRunning;
                if (running == _lastStatus) return;
                _lastStatus = running;
                StatusIndicator.Fill = running ? BrushOnline : BrushOffline;
                StatusText.Text = running ? "MCP Online" : "MCP Offline";
                StatusText.Foreground = running ? BrushOnline : BrushOfflineText;
            }
            catch { }
        }

        private void ChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && !_isProcessing)
            {
                Send_Click(sender, e);
                e.Handled = true;
            }
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string input = ChatInput.Text?.Trim();
            if (string.IsNullOrEmpty(input) || _isProcessing) return;

            ChatInput.Text = "";
            AddMessage("user", input);

            _isProcessing = true;
            SendButton.IsEnabled = false;
            TypingIndicator.Visibility = Visibility.Visible;
            TypingText.Text = "Claude is thinking...";

            try
            {
                if (!Core.SocketService.Instance.IsRunning)
                {
                    AddMessage("assistant",
                        "The MCP server is not running. Click \"Revit MCP Switch\" in the ribbon to start it.");
                    return;
                }

                if (_client == null)
                    _client = new ClaudeRevitClient();
                string response = await _client.SendMessage(input);
                AddMessage("assistant", response);
            }
            catch (Exception ex)
            {
                AddMessage("assistant", $"An error occurred: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                SendButton.IsEnabled = true;
                TypingIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateChips()
        {
            try
            {
                if (!Core.SocketService.Instance.IsRunning)
                {
                    _chips.Clear();
                    ChipsBar.Visibility = Visibility.Collapsed;
                    return;
                }

                var uiApp = Core.SocketService.Instance.UiApplication;
                if (uiApp?.ActiveUIDocument == null)
                {
                    SetChips(new[] { new PromptChip("Open a project to get started") });
                    return;
                }

                var doc = uiApp.ActiveUIDocument.Document;
                var activeView = doc.ActiveView;
                var selection = uiApp.ActiveUIDocument.Selection;
                int selectedCount = selection.GetElementIds().Count;

                var chips = new List<PromptChip>();

                // Selection-based chips (highest priority)
                if (selectedCount > 0)
                {
                    chips.Add(new PromptChip($"Show parameters ({selectedCount} selected)", "Show me the parameters of the selected elements"));
                    chips.Add(new PromptChip("Isolate in view", "Isolate the selected elements in the current view"));
                    chips.Add(new PromptChip("Measure distance", "Measure the distance between the selected elements"));
                }

                // View-type chips
                if (activeView is Autodesk.Revit.DB.ViewPlan)
                {
                    bool hasRooms = new Autodesk.Revit.DB.FilteredElementCollector(doc, activeView.Id)
                        .OfCategory(Autodesk.Revit.DB.BuiltInCategory.OST_Rooms)
                        .GetElementCount() > 0;

                    if (hasRooms)
                    {
                        chips.Add(new PromptChip("Tag all rooms", "Tag all rooms in the current view"));
                        chips.Add(new PromptChip("Color rooms by department", "Create a color legend for rooms by department"));
                        chips.Add(new PromptChip("Export room data", "Export all room data"));
                        chips.Add(new PromptChip("Create callouts for rooms", "Create callout views for all rooms on this level"));
                    }
                    else
                    {
                        chips.Add(new PromptChip("Check model health", "Check the health of this model"));
                        chips.Add(new PromptChip("Show warnings", "Show me all model warnings"));
                        chips.Add(new PromptChip("Export to Excel", "Export all elements in this view to Excel"));
                    }
                }
                else if (activeView is Autodesk.Revit.DB.View3D)
                {
                    chips.Add(new PromptChip("Check model health", "Check the health of this model"));
                    chips.Add(new PromptChip("Detect clashes", "Check for clashes between structural elements and MEP"));
                    chips.Add(new PromptChip("Section box from selection", "Create a section box around the selected elements"));
                    chips.Add(new PromptChip("Audit families", "Audit all families in this project"));
                }
                else if (activeView is Autodesk.Revit.DB.ViewSheet)
                {
                    chips.Add(new PromptChip("Align viewports", "Align all viewports on this sheet"));
                    chips.Add(new PromptChip("Add revision", "Add a revision to this sheet"));
                    chips.Add(new PromptChip("Export to PDF", "Export all sheets to PDF"));
                    chips.Add(new PromptChip("Duplicate sheet", "Duplicate this sheet with all content"));
                }
                else if (activeView is Autodesk.Revit.DB.ViewSchedule)
                {
                    chips.Add(new PromptChip("Export schedule to CSV", "Export this schedule to CSV"));
                    chips.Add(new PromptChip("Export to Excel", "Export this schedule to Excel"));
                }
                else if (activeView is Autodesk.Revit.DB.ViewSection || activeView is Autodesk.Revit.DB.ViewDrafting)
                {
                    chips.Add(new PromptChip("Add dimensions", "Create dimensions in this view"));
                    chips.Add(new PromptChip("Add text note", "Add a text note in this view"));
                    chips.Add(new PromptChip("Export to PDF", "Export all sheets to PDF"));
                }

                // Fallback if no view-specific chips were added
                if (chips.Count == 0 || (selectedCount == 0 && chips.Count < 3))
                {
                    chips.Add(new PromptChip("Model statistics", "How many elements are in this model? Give me statistics by category"));
                    chips.Add(new PromptChip("Check model health", "Check the health of this model"));
                    chips.Add(new PromptChip("Export to Excel", "Export all elements to Excel"));
                    chips.Add(new PromptChip("List warnings", "Show me all model warnings"));
                }

                // Limit to 6
                SetChips(chips.Take(6));
            }
            catch
            {
                // Never crash the panel due to chip updates
                _chips.Clear();
                ChipsBar.Visibility = Visibility.Collapsed;
            }
        }

        private void SetChips(IEnumerable<PromptChip> chips)
        {
            _chips.Clear();
            foreach (var chip in chips)
                _chips.Add(chip);
            ChipsBar.Visibility = _chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Chip_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PromptChip chip && !_isProcessing)
            {
                ChatInput.Text = chip.Prompt;
                Send_Click(sender, e);
            }
        }

        private void AddMessage(string role, string text)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _messages.Add(new ChatMessage(role, text));
                ChatScrollViewer.ScrollToEnd();
            }));
        }

        public void OnToolExecuting(string toolName)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TypingText.Text = $"Running {toolName}...";
                _messages.Add(new ChatMessage("tool", $"⚡ {toolName}"));
                ChatScrollViewer.ScrollToEnd();
            }));
        }

        public void OnToolCompleted(string toolName, bool isError, string result)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string preview = result.Length > 200 ? result.Substring(0, 200) + "..." : result;
                string status = isError ? $"✗ {toolName} — error:\n{preview}" : $"✓ {toolName} completed";
                _messages.Add(new ChatMessage(isError ? "tool_error" : "tool_ok", status));
                ChatScrollViewer.ScrollToEnd();
            }));
        }

        public void OnIntermediateText(string text)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _messages.Add(new ChatMessage("assistant", text));
                ChatScrollViewer.ScrollToEnd();
            }));
        }

        public void OnRoundProgress(int current, int max)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TypingText.Text = $"Claude is processing... (step {current}/{max})";
            }));
        }

        public void LogCommand(string commandName, bool success, string message, double durationMs) { }


        public void OnRetrying(int seconds)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TypingText.Text = $"Rate limit — retrying in {seconds}s...";
            }));
        }

        public void OnThinkingReceived(string thinkingText)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Show a compact summary instead of the full thinking text
                int charCount = thinkingText.Length;
                string firstLine = thinkingText.Split('\n')[0];
                if (firstLine.Length > 120)
                    firstLine = firstLine.Substring(0, 120) + "...";
                string summary = $"{firstLine}\n[{charCount:N0} chars of reasoning]";
                _messages.Add(new ChatMessage("thinking", summary));
                ChatScrollViewer.ScrollToEnd();
            }));
        }

        private void StopButton_Click(object sender, MouseButtonEventArgs e)
        {
            _client?.Cancel();
            TypingText.Text = "Cancelling...";
        }

        private void ClearChat_Click(object sender, MouseButtonEventArgs e)
        {
            _messages.Clear();
            _client?.ClearHistory();
            AddMessage("assistant", "Chat cleared. How can I help you?");
        }

        private void ExportChat_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export chat",
                    FileName = $"chat_{DateTime.Now:yyyyMMdd_HHmmss}",
                    DefaultExt = ".txt",
                    Filter = "Text (*.txt)|*.txt|Markdown (*.md)|*.md|JSON (*.json)|*.json",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (dialog.ShowDialog() != true) return;

                string content;
                switch (dialog.FilterIndex)
                {
                    case 2: content = BuildMarkdown(); break;
                    case 3: content = BuildJson(); break;
                    default: content = BuildPlainText(); break;
                }

                File.WriteAllText(dialog.FileName, content, Encoding.UTF8);
                AddMessage("assistant", $"Chat exported to:\n{dialog.FileName}");
            }
            catch (Exception ex)
            {
                AddMessage("assistant", $"Export error: {ex.Message}");
            }
        }

        public void AddSystemMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                _messages.Add(new ChatMessage("tool", message));
                ChatScrollViewer.ScrollToEnd();
            });
        }

        private void VerifyConnection_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var pluginDir = Path.GetDirectoryName(typeof(MCPDockablePanel).Assembly.Location);
                int port = SocketService.Instance?.Port ?? 0;
                var results = HealthChecker.RunAll(pluginDir, port);

                var sb = new StringBuilder();
                sb.AppendLine("--- Connection Diagnostic ---");
                foreach (var r in results)
                {
                    string icon = r.Passed ? "[OK]" : "[FAIL]";
                    if (r.AutoFixed) icon = "[FIXED]";
                    sb.AppendLine($"{icon} {r.Name}: {r.Message}");
                }
                sb.AppendLine($"Log folder: {McpLogger.LogDirectory ?? "N/A"}");
                sb.Append("-----------------------------");

                AddSystemMessage(sb.ToString());
            }
            catch (Exception ex)
            {
                AddSystemMessage($"[ERROR] Diagnostic failed: {ex.Message}");
            }
        }

        private string BuildPlainText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Claude for Revit — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();
            foreach (var msg in _messages)
            {
                sb.AppendLine($"[{msg.RoleLabel}]");
                sb.AppendLine(msg.Text);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private string BuildMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Claude for Revit — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            foreach (var msg in _messages)
            {
                sb.AppendLine($"**{msg.RoleLabel}**");
                sb.AppendLine();
                sb.AppendLine(msg.Text);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private string BuildJson()
        {
            var array = new Newtonsoft.Json.Linq.JArray();
            foreach (var msg in _messages)
                array.Add(new Newtonsoft.Json.Linq.JObject
                {
                    ["role"] = msg.RoleLabel,
                    ["text"] = msg.Text
                });
            return new Newtonsoft.Json.Linq.JObject
            {
                ["exported"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["messages"] = array
            }.ToString(Newtonsoft.Json.Formatting.Indented);
        }
    }

    public class PromptChip
    {
        public string Text { get; set; }
        public string Prompt { get; set; }

        public PromptChip(string text, string prompt = null)
        {
            Text = text;
            Prompt = prompt ?? text;
        }
    }

    public class ChatMessage
    {
        public string Role { get; }
        public string Text { get; }
        public string RoleLabel { get; }
        public string AvatarLetter { get; }
        public SolidColorBrush AvatarBackground { get; }
        public SolidColorBrush RoleLabelColor { get; }
        public SolidColorBrush TextColor { get; }
        public SolidColorBrush RowBackground { get; }
        public FontFamily FontFamily { get; }

        private static readonly SolidColorBrush ClaudeOrange = new SolidColorBrush(Color.FromRgb(217, 119, 87));
        private static readonly SolidColorBrush UserBlue = new SolidColorBrush(Color.FromRgb(88, 130, 207));
        private static readonly SolidColorBrush ToolGreen = new SolidColorBrush(Color.FromRgb(76, 175, 80));
        private static readonly SolidColorBrush ThinkingPurple = new SolidColorBrush(Color.FromRgb(147, 112, 219));

        public ChatMessage(string role, string text)
        {
            Role = role;
            Text = text;

            switch (role)
            {
                case "user":
                    RoleLabel = "You";
                    AvatarLetter = "L";
                    AvatarBackground = UserBlue;
                    RoleLabelColor = new SolidColorBrush(Color.FromRgb(26, 26, 26));
                    TextColor = new SolidColorBrush(Color.FromRgb(26, 26, 26));
                    RowBackground = new SolidColorBrush(Colors.White);
                    FontFamily = new FontFamily("Segoe UI");
                    break;
                case "thinking":
                    RoleLabel = "Planning";
                    AvatarLetter = "💡";
                    AvatarBackground = ThinkingPurple;
                    RoleLabelColor = ThinkingPurple;
                    TextColor = new SolidColorBrush(Color.FromRgb(120, 100, 160));
                    RowBackground = new SolidColorBrush(Color.FromRgb(248, 246, 252));
                    FontFamily = new FontFamily("Segoe UI");
                    break;
                case "tool":
                    RoleLabel = "";
                    AvatarLetter = "⚡";
                    AvatarBackground = ToolGreen;
                    RoleLabelColor = ToolGreen;
                    TextColor = new SolidColorBrush(Color.FromRgb(46, 125, 50));
                    RowBackground = new SolidColorBrush(Color.FromRgb(250, 249, 247));
                    FontFamily = new FontFamily("Consolas");
                    break;
                case "tool_ok":
                    RoleLabel = "";
                    AvatarLetter = "✓";
                    AvatarBackground = ToolGreen;
                    RoleLabelColor = ToolGreen;
                    TextColor = new SolidColorBrush(Color.FromRgb(46, 125, 50));
                    RowBackground = new SolidColorBrush(Color.FromRgb(245, 252, 245));
                    FontFamily = new FontFamily("Segoe UI");
                    break;
                case "tool_error":
                    RoleLabel = "";
                    AvatarLetter = "✗";
                    AvatarBackground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                    RoleLabelColor = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                    TextColor = new SolidColorBrush(Color.FromRgb(183, 28, 28));
                    RowBackground = new SolidColorBrush(Color.FromRgb(255, 245, 245));
                    FontFamily = new FontFamily("Consolas");
                    break;
                default:
                    RoleLabel = "Claude";
                    AvatarLetter = "C";
                    AvatarBackground = ClaudeOrange;
                    RoleLabelColor = new SolidColorBrush(Color.FromRgb(26, 26, 26));
                    TextColor = new SolidColorBrush(Color.FromRgb(55, 53, 47));
                    RowBackground = new SolidColorBrush(Color.FromRgb(250, 249, 247));
                    FontFamily = new FontFamily("Segoe UI");
                    break;
            }
        }
    }
}
