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
    private TextBlock? _streamTextBlock;
    private DispatcherTimer? _streamTimer;
    private DispatcherTimer? _deactivateTimer;

    private static readonly SolidColorBrush UserBubbleBrush = new(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly SolidColorBrush AiBubbleBrush = new(Color.FromRgb(0xF2, 0xF2, 0xF7));
    private static readonly SolidColorBrush TextDarkBrush = new(Color.FromRgb(0x1F, 0x1F, 0x1F));

    public ChatWindow()
    {
        InitializeComponent();

        ModeCombo.Items.Add("回答问题");
        ModeCombo.Items.Add("解释");
        ModeCombo.Items.Add("翻译");
        ModeCombo.Items.Add("润色");
        ModeCombo.Items.Add("自定义");
        ModeCombo.SelectedIndex = 0;

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

        // Keyboard-first flow: when the mode selector has focus, Up/Down pick
        // the action and Enter sends the message right away.
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
        }

        Activate();

        if (hasSelection && settings.AutoSendOnSelection)
        {
            // Automatic flow: send immediately, then focus the input box.
            HintText.Text = "Ctrl+Enter 发送 · Esc 隐藏";
            SendMessage();
            InputBox.Focus();
            return;
        }

        if (hasSelection)
        {
            // Keyboard-first flow: pick an action with Up/Down, Enter to send.
            HintText.Text = "↑/↓ 选择操作 · 回车发送 · Esc 隐藏";
            ModeCombo.Focus();
            return;
        }

        HintText.Text = "Ctrl+Enter 发送 · Esc 隐藏";
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
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
        var systemPrompt = BuildSystemPrompt(ModeCombo.SelectedIndex, settings.CustomPrompt);
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            history.Add(new ChatMessage("system", systemPrompt));
        }
        history.AddRange(_messages);

        _cts = new CancellationTokenSource();
        _isGenerating = true;
        UpdateSendButton();

        // Create the assistant bubble (updated incrementally as tokens arrive).
        var aiText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = TextDarkBrush
        };
        var aiBorder = new Border
        {
            Background = AiBubbleBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 4),
            MaxWidth = 380,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = aiText
        };
        MessagesPanel.Children.Add(aiBorder);
        _streamTextBlock = aiText;
        _streamBuffer = new StringBuilder();
        ScrollToBottom();

        var token = _cts.Token;
        var snapshot = new List<ChatMessage>(history);
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var chunk in _chat.StreamChatAsync(
                    settings.BaseUrl, settings.ApiKey, settings.Model, settings.Temperature,
                    snapshot, token))
                {
                    var c = chunk;
                    Dispatcher.Invoke(() => AppendStreamChunk(c));
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

    private void AppendStreamChunk(string chunk)
    {
        if (_streamBuffer == null)
        {
            return;
        }

        _streamBuffer.Append(chunk);

        if (_streamTimer == null)
        {
            _streamTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _streamTimer.Tick += (_, _) => FlushStream();
            _streamTimer.Start();
        }
    }

    private void FlushStream()
    {
        if (_streamBuffer == null || _streamTextBlock == null)
        {
            return;
        }

        _streamTextBlock.Text = _streamBuffer.ToString();
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

        _streamBuffer = null;
        _streamTextBlock = null;
        _isGenerating = false;
        UpdateSendButton();
    }

    private void StopGeneration()
    {
        _cts?.Cancel();
    }

    private void UpdateSendButton()
    {
        SendButton.Content = _isGenerating ? "停止" : "发送 (Ctrl+Enter)";
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

    private static string BuildSystemPrompt(int modeIndex, string customPrompt)
    {
        return modeIndex switch
        {
            0 => "你是一个乐于助人的 AI 助手。请用简体中文回答，回答尽量简洁、准确。",
            1 => "请解释用户提供的内容，用简体中文回答，条理清晰，通俗易懂。",
            2 => "请将用户提供的内容翻译成简体中文；如果原文已经是中文，则翻译成英文。只输出译文，不要额外说明。",
            3 => "请润色用户提供的文字，使其更通顺、专业、简洁，保持原意，用简体中文输出。",
            4 => customPrompt,
            _ => "你是一个乐于助人的 AI 助手。"
        };
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
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
