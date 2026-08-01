using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AskAnywhere;
using AskAnywhere.Services;

namespace AskAnywhere.Views;

public partial class ChatWindow : Window
{
    private readonly ChatService _chat = new();
    private readonly List<ChatMessage> _messages = new();
    private CancellationTokenSource? _cts;
    private bool _isGenerating;

    private StringBuilder? _streamBuffer;
    private StringBuilder? _streamReasoning;
    private TextBlock? _streamTextBlock;
    private TextBlock? _streamReasoningBlock;
    private DispatcherTimer? _streamTimer;
    private DispatcherTimer? _deactivateTimer;

    // True while the "pick an action with Up/Down" flow is active (right after
    // the window opens with a selection). Editing the input box disables it.
    private bool _modePickerActive;

    private static readonly SolidColorBrush UserBubbleBrush = new(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly SolidColorBrush AiBubbleBrush = new(Color.FromRgb(0xF2, 0xF2, 0xF7));
    private static readonly SolidColorBrush TextDarkBrush = new(Color.FromRgb(0x1F, 0x1F, 0x1F));
    private static readonly SolidColorBrush ReasoningBrush = new(Color.FromRgb(0x8A, 0x8A, 0x8A));

    public ChatWindow()
    {
        InitializeComponent();

        InputBox.PreviewMouseDown += (_, _) => _modePickerActive = false;
        InputBox.GotKeyboardFocus += (_, _) => _modePickerActive = false;

        PreviewKeyDown += ChatWindow_PreviewKeyDown;
        Deactivated += ChatWindow_Deactivated;
    }

    private void ChatWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_isGenerating)
            {
                StopGeneration();
            }
            else
            {
                Hide();
            }
            return;
        }

        // Keyboard-first flow: while the mode picker is active, Up/Down switch
        // the action and Enter sends right away.
        if (_modePickerActive)
        {
            if (e.Key == Key.Up)
            {
                e.Handled = true;
                MoveMode(-1);
            }
            else if (e.Key == Key.Down)
            {
                e.Handled = true;
                MoveMode(1);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SendMessage();
            }
            return;
        }

        // Generic support when the mode combo itself has focus.
        if (ModeCombo.IsKeyboardFocusWithin && !ModeCombo.IsDropDownOpen)
        {
            if (e.Key == Key.Up)
            {
                e.Handled = true;
                MoveMode(-1);
            }
            else if (e.Key == Key.Down)
            {
                e.Handled = true;
                MoveMode(1);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SendMessage();
            }
        }
    }

    /// <summary>
    /// Called right before/after the window is shown. Positions the window near
    /// the cursor, clears the previous keyword, captures any new selected text,
    /// and focuses the right control for the current flow.
    /// </summary>
    public async void PrepareForShow()
    {
        PositionNearCursor();
        ReloadModes();

        // Fresh state on every open: clear the keyword from the last session.
        InputBox.Clear();

        string? selected = null;
        try
        {
            selected = await SelectionCaptureService.GetSelectedTextAsync(CancellationToken.None);
        }
        catch
        {
            // Ignore capture failures.
        }

        bool hasSelection = !string.IsNullOrWhiteSpace(selected);
        var settings = SettingsService.Instance.Current;

        if (hasSelection)
        {
            InputBox.Text = selected;
            // Place the caret at the end for easy editing.
            InputBox!.CaretIndex = InputBox!.Text.Length;
        }

        Activate();

        if (hasSelection && settings.AutoSendOnSelection)
        {
            HintText.Text = "Enter 发送 · Shift+Enter 换行 · Esc 隐藏";
            _modePickerActive = false;
            SendMessage();
            InputBox.Focus();
            return;
        }

        if (hasSelection)
        {
            // Keyboard-first flow: pick an action with Up/Down, Enter to send.
            _modePickerActive = true;
            HintText.Text = "↑/↓ 选择操作 · 回车发送 · Esc 隐藏";
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsVisible && _modePickerActive)
                {
                    ModeCombo.Focus();
                }
            }));
            return;
        }

        _modePickerActive = false;
        HintText.Text = "Enter 发送 · Shift+Enter 换行 · Esc 隐藏";
        InputBox.Focus();
    }

    private void ReloadModes()
    {
        var current = ModeCombo.SelectedItem?.ToString() ?? "";
        ModeCombo.Items.Clear();

        var modes = SettingsService.Instance.Current.Modes;
        foreach (var m in modes)
        {
            ModeCombo.Items.Add(m.Name);
        }

        int idx = 0;
        if (!string.IsNullOrEmpty(current))
        {
            int found = ModeCombo.Items.IndexOf(current);
            if (found >= 0)
            {
                idx = found;
            }
        }
        if (ModeCombo.Items.Count > 0)
        {
            ModeCombo.SelectedIndex = Math.Min(idx, ModeCombo.Items.Count - 1);
        }
    }

    private void PositionNearCursor()
    {
        try
        {
            var pos = NativeMethods.GetCursorPosition();
            var wa = SystemParameters.WorkArea;
            double x = pos.X + 14;
            double y = pos.Y + 14;

            if (x + Width > wa.Right - 8)
            {
                x = pos.X - Width - 14;
            }
            if (y + Height > wa.Bottom - 8)
            {
                y = wa.Bottom - Height - 8;
            }

            Left = Math.Max(wa.Left + 8, x);
            Top = Math.Max(wa.Top + 8, y);
        }
        catch
        {
            // Keep the current position on failure.
        }
    }

    private void MoveMode(int delta)
    {
        int count = ModeCombo.Items.Count;
        if (count == 0)
        {
            return;
        }

        int idx = ModeCombo.SelectedIndex;
        if (idx < 0)
        {
            idx = 0;
        }
        idx = (idx + delta + count) % count;
        ModeCombo.SelectedIndex = idx;
    }

    private void SendMessage()
    {
        if (_isGenerating)
        {
            return;
        }

        var text = InputBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var settings = SettingsService.Instance.Current;
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            ShowStatus("请先在设置中填写 API Key");
            return;
        }

        InputBox.Clear();
        AddMessage("user", text);
        _messages.Add(new ChatMessage("user", text));

        // Build the request messages: optional system prompt + full history.
        var history = new List<ChatMessage>();
        var systemPrompt = BuildSystemPrompt(ModeCombo.SelectedIndex);
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            history.Add(new ChatMessage("system", systemPrompt));
        }
        history.AddRange(_messages);

        _cts = new CancellationTokenSource();
        _isGenerating = true;
        UpdateSendButton();

        // Create the assistant bubble: reasoning block (grey, when enabled)
        // above the final answer block.
        var panel = new StackPanel();
        _streamReasoningBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = ReasoningBrush,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        };
        _streamTextBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = TextDarkBrush
        };
        panel.Children.Add(_streamReasoningBlock);
        panel.Children.Add(_streamTextBlock);

        var aiBorder = new Border
        {
            Background = AiBubbleBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 4),
            MaxWidth = 380,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = panel
        };
        MessagesPanel.Children.Add(aiBorder);
        _streamBuffer = new StringBuilder();
        _streamReasoning = new StringBuilder();
        ScrollToBottom();

        var token = _cts.Token;
        var snapshot = new List<ChatMessage>(history);
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var delta in _chat.StreamChatAsync(
                    settings.BaseUrl, settings.ApiKey, settings.Model, settings.Temperature,
                    settings.ThinkingEnabled, settings.ThinkingBudgetTokens,
                    snapshot, token))
                {
                    var d = delta;
                    Dispatcher.Invoke(() => AppendStreamChunk(d));
                }
                Dispatcher.Invoke(() => FinishStream(true));
            }
            catch (OperationCanceledException)
            {
                Dispatcher.Invoke(() => FinishStream(false, true, null));
            }
            catch (Exception ex)
            {
                try
                {
                    Dispatcher.Invoke(() => FinishStream(false, false, ex.Message));
                }
                catch
                {
                    // Window may already be shutting down.
                }
            }
        });
    }

    private void AppendStreamChunk(ChatDelta delta)
    {
        if (delta.Reasoning != null && _streamReasoning != null)
        {
            _streamReasoning.Append(delta.Reasoning);
        }
        if (delta.Content != null && _streamBuffer != null)
        {
            _streamBuffer.Append(delta.Content);
        }

        if (_streamTimer == null)
        {
            _streamTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _streamTimer.Tick += (_, _) => FlushStream();
            _streamTimer.Start();
        }
    }

    private void FlushStream()
    {
        if (_streamReasoningBlock != null && _streamReasoning != null && _streamReasoning.Length > 0)
        {
            _streamReasoningBlock.Text = "💭 " + _streamReasoning;
        }
        if (_streamTextBlock != null && _streamBuffer != null)
        {
            _streamTextBlock.Text = _streamBuffer.ToString();
        }
        ScrollToBottom();
    }

    private void FinishStream(bool success, bool cancelled = false, string? error = null)
    {
        if (_streamTimer != null)
        {
            _streamTimer.Stop();
            _streamTimer = null;
        }
        FlushStream();

        var finalText = _streamBuffer?.ToString() ?? "";
        var finalReasoning = _streamReasoning?.ToString() ?? "";

        if (success)
        {
            if (finalText.Length > 0)
            {
                _messages.Add(new ChatMessage("assistant", finalText));
            }
        }
        else if (cancelled)
        {
            if (finalText.Length > 0)
            {
                _messages.Add(new ChatMessage("assistant", finalText + "\n\n[已停止]"));
            }
            ShowStatus("已停止");
        }
        else
        {
            ShowStatus("请求失败: " + (error ?? "未知错误"));
            if (_streamTextBlock != null)
            {
                _streamTextBlock.Text = finalText
                    + (finalText.Length > 0 ? "\n\n" : "")
                    + "⚠ 请求失败: " + error;
            }
        }

        _ = finalReasoning;
        _streamBuffer = null;
        _streamReasoning = null;
        _streamTextBlock = null;
        _streamReasoningBlock = null;
        _isGenerating = false;
        UpdateSendButton();
    }

    private void StopGeneration()
    {
        _cts?.Cancel();
    }

    private void UpdateSendButton()
    {
        SendButton.Content = _isGenerating ? "停止" : "发送";
    }

    private void AddMessage(string role, string content)
    {
        var tb = new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap,
            Foreground = role == "user" ? Brushes.White : TextDarkBrush
        };
        var border = new Border
        {
            Background = role == "user" ? UserBubbleBrush : AiBubbleBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 4),
            MaxWidth = 380,
            HorizontalAlignment = role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = tb
        };
        MessagesPanel.Children.Add(border);
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        Dispatcher.BeginInvoke(new Action(() => MessagesScroll.ScrollToEnd()), DispatcherPriority.Background);
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
    }

    private static string BuildSystemPrompt(int modeIndex)
    {
        var modes = SettingsService.Instance.Current.Modes;
        if (modeIndex >= 0 && modeIndex < modes.Count)
        {
            return modes[modeIndex].Prompt ?? "";
        }
        return "";
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                // Shift+Enter inserts a newline.
                return;
            }
            e.Handled = true;
            if (_isGenerating)
            {
                StopGeneration();
            }
            else
            {
                SendMessage();
            }
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isGenerating)
        {
            StopGeneration();
        }
        else
        {
            SendMessage();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _messages.Clear();
        MessagesPanel.Children.Clear();
        InputBox.Clear();
        ShowStatus("");
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ShowSettingsWindow();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isGenerating)
        {
            StopGeneration();
        }
        Hide();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>
    /// Auto-hide when the window loses focus (unless another window of this
    /// app, e.g. Settings, took the focus).
    /// </summary>
    private void ChatWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!SettingsService.Instance.Current.AutoHideOnDeactivate)
        {
            return;
        }

        _deactivateTimer?.Stop();
        _deactivateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _deactivateTimer.Tick += DeactivateTimer_Tick;
        _deactivateTimer.Start();
    }

    private void DeactivateTimer_Tick(object? sender, EventArgs e)
    {
        if (_deactivateTimer != null)
        {
            _deactivateTimer.Stop();
            _deactivateTimer = null;
        }

        if (IsActive || AnyAppWindowActive())
        {
            return;
        }

        Hide();
    }

    private static bool AnyAppWindowActive()
    {
        foreach (Window w in Application.Current.Windows)
        {
            if (w.IsVisible && w.IsActive)
            {
                return true;
            }
        }
        return false;
    }
}
