using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AskAnywhere;
using AskAnywhere.Models;
using AskAnywhere.Services;

namespace AskAnywhere.Views;

public partial class ChatWindow : Window
{
    private readonly ChatService _chat = new();
    private readonly WebSearchService _search = new();
    private readonly List<ChatMessage> _messages = new();
    private CancellationTokenSource? _cts;
    private bool _isGenerating;

    private StringBuilder? _streamBuffer;
    private StringBuilder? _streamReasoning;
    private List<SearchResult>? _lastSearchResults;
    private string? _lastQuery;
    private string? _lastSearchProvider;
    private TextBlock? _streamTextBlock;
    private TextBlock? _streamReasoningBlock;
    private StackPanel? _streamPanel;
    private DispatcherTimer? _streamTimer;
    private DispatcherTimer? _deactivateTimer;

    // While true, Up/Down switch the chat action and Enter sends, no matter
    // which control has keyboard focus. It stays active for the whole time the
    // window is open, so the user can type and switch actions freely.
    private bool _modePickerActive;

    private static readonly SolidColorBrush UserBubbleBrush = new(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly SolidColorBrush AiBubbleBrush = new(Color.FromRgb(0xF2, 0xF2, 0xF7));
    private static readonly SolidColorBrush TextDarkBrush = new(Color.FromRgb(0x1F, 0x1F, 0x1F));
    private static readonly SolidColorBrush ReasoningBrush = new(Color.FromRgb(0x8A, 0x8A, 0x8A));
    // Hairline borders replace DropShadowEffect: any WPF Effect forces the
    // bubble onto an off-screen bitmap, which disables ClearType and makes
    // text look blurry.
    private static readonly SolidColorBrush AiBubbleBorderBrush = new(Color.FromRgb(0xE4, 0xE6, 0xEB));
    private static readonly SolidColorBrush UserBubbleBorderBrush = new(Color.FromRgb(0x17, 0x3F, 0xB5));

    // View labels map to the SearchProvider codes used by WebSearchService.
    private static readonly (string Label, string Code)[] SearchProviders =
    {
        ("Tavily", "Tavily"),
        ("Google", "Google"),
        ("知乎", "Zhihu"),
        ("自定义", "Custom")
    };

    public ChatWindow()
    {
        InitializeComponent();
        PreviewKeyDown += ChatWindow_PreviewKeyDown;
        Deactivated += ChatWindow_Deactivated;
        ModelCombo.SelectionChanged += (_, _) => SaveModelSelection();
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
                HideChatWindow();
            }
            return;
        }

        // Keyboard-first flow: while the picker is active, Up/Down switch the
        // action and Enter sends right away (also when the input box has focus).
        if (_modePickerActive && !ModeCombo.IsDropDownOpen)
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
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                // Shift+Enter falls through to the input box and inserts a newline.
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
            return;
        }

        // Enter sends from the input box. Must be intercepted here (Preview)
        // because the TextBox swallows Enter internally in the bubbling phase.
        if (e.Key == Key.Enter && InputBox.IsKeyboardFocusWithin)
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
    /// the cursor, captures any new selected text, and focuses the input box.
    /// The conversation was already cleared when the window was hidden.
    /// </summary>
    public async void PrepareForShow()
    {
        PositionNearCursor();
        ReloadProviders();
        ReloadModes();

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

        // The temporary reasoning toggle and search mode start from the saved
        // settings; they can be flipped for a single conversation.
        TempThinkingCheck.IsChecked = settings.ThinkingEnabled;
        SearchModeCombo.SelectedIndex = settings.SearchMode?.Trim().ToLowerInvariant() switch
        {
            "always" => 1,
            "off" => 2,
            _ => 0
        };
        ReloadSearchProviders();

        if (hasSelection)
        {
            InputBox.Text = selected;
        }

        // Up/Down always switch the action (even while typing); Enter sends.
        _modePickerActive = true;
        HintText.Text = "↑/↓ 切换功能 · Enter 发送 · Esc 隐藏";

        Activate();

        if (hasSelection && settings.AutoSendOnSelection)
        {
            SendMessage();
            return;
        }

        // The input box gets focus so the user can type right away; Up/Down
        // still switch the action because _modePickerActive stays true.
        InputBox.Focus();
        InputBox!.CaretIndex = InputBox!.Text?.Length ?? 0;
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

    private void ReloadProviders()
    {
        var current = ProviderCombo.SelectedItem?.ToString() ?? "";
        ProviderCombo.Items.Clear();
        foreach (var p in SettingsService.Instance.Current.Providers)
        {
            ProviderCombo.Items.Add(p.Name);
        }

        int idx = 0;
        if (!string.IsNullOrEmpty(current))
        {
            int found = ProviderCombo.Items.IndexOf(current);
            if (found >= 0)
            {
                idx = found;
            }
        }
        if (ProviderCombo.Items.Count > 0)
        {
            ProviderCombo.SelectedIndex = Math.Min(idx, ProviderCombo.Items.Count - 1);
        }
        ReloadModel();
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ProviderCombo.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(selected))
        {
            SettingsService.Instance.Update(settings => settings.CurrentProvider = selected, out _);
        }
        ReloadModel();
    }

    private void ReloadModel()
    {
        var provider = SettingsService.Instance.CurrentProvider();
        if (provider.Kind == "Zhihu")
        {
            foreach (var m in ChatService.ZhihuModels)
            {
                if (!ModelCombo.Items.Contains(m))
                {
                    ModelCombo.Items.Add(m);
                }
            }
        }
        else if (!string.IsNullOrEmpty(provider.Model) && ModelCombo.Items.IndexOf(provider.Model) < 0)
        {
            ModelCombo.Items.Add(provider.Model);
        }
        ModelCombo.Text = provider.Model;
    }

    /// <summary>Fills the search-source combo from the settings default.</summary>
    private void ReloadSearchProviders()
    {
        var settingsCode = SettingsService.Instance.Current.SearchProvider?.Trim().ToLowerInvariant() switch
        {
            "google" => "Google",
            "custom" => "Custom",
            "zhihu" => "Zhihu",
            _ => "Tavily"
        };
        int idx = Array.FindIndex(SearchProviders, s => s.Code == settingsCode);

        SearchProviderCombo.Items.Clear();
        foreach (var sp in SearchProviders)
        {
            SearchProviderCombo.Items.Add(sp.Label);
        }
        SearchProviderCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    /// <summary>Returns the selected search-source code (falls back to settings).</summary>
    private string GetSearchProviderCode()
    {
        int i = SearchProviderCombo.SelectedIndex;
        if (i >= 0 && i < SearchProviders.Length)
        {
            return SearchProviders[i].Code;
        }
        var s = SettingsService.Instance.Current.SearchProvider?.Trim().ToLowerInvariant();
        return s switch
        {
            "google" => "Google",
            "custom" => "Custom",
            "zhihu" => "Zhihu",
            _ => "Tavily"
        };
    }

    /// <summary>Persists the selected model to the current provider.</summary>
    private void SaveModelSelection(string? model = null)
    {
        var m = (model ?? ModelCombo.Text)?.Trim();
        if (string.IsNullOrEmpty(m))
        {
            return;
        }
        SettingsService.Instance.Update(settings =>
        {
            var p = settings.Providers.FirstOrDefault(x => x.Name == settings.CurrentProvider)
                ?? settings.Providers.FirstOrDefault();
            if (p != null)
            {
                p.Model = m;
            }
        }, out _);
    }

    private async void FetchModelsButton_Click(object sender, RoutedEventArgs e)
    {
        var provider = SettingsService.Instance.CurrentProvider();

        // Zhihu (Zhida) exposes a fixed set of models, no /models endpoint.
        if (provider.Kind == "Zhihu")
        {
            FetchModelsButton.IsEnabled = false;
            try
            {
                var current = ModelCombo.Text;
                ModelCombo.Items.Clear();
                foreach (var m in ChatService.ZhihuModels)
                {
                    ModelCombo.Items.Add(m);
                }
                ModelCombo.Text = !string.IsNullOrEmpty(current) && ChatService.ZhihuModels.Contains(current)
                    ? current
                    : ChatService.ZhihuModels[1];
                SaveModelSelection();
                ShowStatus("知乎直答模型 " + ChatService.ZhihuModels.Length + " 个");
            }
            finally
            {
                FetchModelsButton.IsEnabled = true;
            }
            return;
        }

        if (string.IsNullOrEmpty(provider.BaseUrl) || string.IsNullOrEmpty(provider.ApiKey))
        {
            ShowStatus("请先在设置中填写 Base URL 和 API Key");
            return;
        }

        try
        {
            FetchModelsButton.IsEnabled = false;
            var models = await _chat.GetModelsAsync(provider.BaseUrl, provider.ApiKey, CancellationToken.None);

            var current = ModelCombo.Text;
            ModelCombo.Items.Clear();
            foreach (var m in models)
            {
                ModelCombo.Items.Add(m);
            }
            ModelCombo.Text = !string.IsNullOrEmpty(current) && models.Contains(current)
                ? current
                : (models.Count > 0 ? models[0] : current);

            SaveModelSelection();
            ShowStatus("获取到 " + models.Count + " 个模型");
        }
        catch (Exception ex)
        {
            ShowStatus("获取模型失败: " + ex.Message);
        }
        finally
        {
            FetchModelsButton.IsEnabled = true;
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

    /// <summary>Persists the current conversation to history (no-op when empty).</summary>
    public void SaveCurrentSession()
    {
        if (_messages.Count == 0)
        {
            return;
        }
        var session = new ChatSession
        {
            CreatedAt = DateTime.Now,
            Messages = new List<ChatMessage>(_messages)
        };
        HistoryService.Add(session);
    }

    /// <summary>
    /// Saves the current conversation to history, resets the conversation and
    /// hides the window. Re-opening always starts a brand new conversation.
    /// </summary>
    public void HideChatWindow()
    {
        SaveCurrentSession();
        _messages.Clear();
        MessagesPanel.Children.Clear();
        InputBox.Clear();
        _modePickerActive = false;
        _lastSearchResults = null;
        _lastQuery = null;
        _lastSearchProvider = null;
        ShowStatus("");
        Hide();
    }

    private async void SendMessage()
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
        var provider = SettingsService.Instance.CurrentProvider();
        bool isZhihu = provider.Kind == "Zhihu";
        if (isZhihu && string.IsNullOrEmpty(settings.ZhihuAccessSecret))
        {
            ShowStatus("请先在设置中填写知乎 Access Secret");
            return;
        }
        if (!isZhihu && string.IsNullOrEmpty(provider.ApiKey))
        {
            ShowStatus("请先在设置中填写 API Key");
            return;
        }

        var model = ModelCombo.Text?.Trim();
        if (string.IsNullOrEmpty(model))
        {
            model = provider.Model;
        }
        SaveModelSelection(model);

        InputBox.Clear();
        AddMessage("user", text);
        _messages.Add(new ChatMessage("user", text));

        _cts = new CancellationTokenSource();
        _isGenerating = true;
        UpdateSendButton();

        // Create the assistant bubble: reasoning block (grey, when enabled)
        // above the final answer block. It is created FIRST so the user sees
        // immediate feedback (e.g. "正在搜索…") instead of a silent wait.
        var panel = new StackPanel();
        _streamPanel = panel;
        _streamReasoningBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = ReasoningBrush,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        };
        ApplySharpText(_streamReasoningBlock);
        _streamTextBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = TextDarkBrush
        };
        ApplySharpText(_streamTextBlock);
        panel.Children.Add(_streamReasoningBlock);
        panel.Children.Add(_streamTextBlock);

        var aiBorder = new Border
        {
            Background = AiBubbleBrush,
            BorderBrush = AiBubbleBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
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

        // Optional web search. Mode: 自动 (Auto) / 始终 (Always) / 关闭 (Off).
        string? searchContext = null;
        _lastSearchResults = null;
        _lastQuery = null;
        _lastSearchProvider = null;
        var searchMode = SearchModeCombo.SelectedItem?.ToString() ?? "自动";
        bool doSearch = searchMode switch
        {
            "始终" => true,
            "关闭" => false,
            _ => ShouldSearchAuto(text)
        };

        if (doSearch)
        {
            _streamTextBlock.Text = "🔍 正在搜索…";
            ShowStatus("正在搜索…");
            var query = text.Length > 150 ? text.Substring(0, 150) : text;
            var s = SettingsService.Instance.Current;
            var searchProvider = GetSearchProviderCode();
            try
            {
                var results = await Task.Run(() =>
                    _search.SearchAsync(query, searchProvider, s.SearchApiKey, s.GoogleSearchApiKey, s.CustomSearchUrl, s.ZhihuAccessSecret, _cts.Token));
                if (results.Count > 0)
                {
                    searchContext = BuildSearchContext(results);
                    _lastSearchResults = results;
                    _lastQuery = query;
                    _lastSearchProvider = searchProvider;
                    ShowStatus("已找到 " + results.Count + " 条结果");
                }
                else
                {
                    ShowStatus("未找到相关搜索结果");
                }
            }
            catch (OperationCanceledException)
            {
                _streamTextBlock.Text = "";
                _isGenerating = false;
                UpdateSendButton();
                ShowStatus("");
                return;
            }
            catch (Exception ex)
            {
                ShowStatus("搜索失败: " + ex.Message);
            }
            // Clear the placeholder; streaming content will replace it.
            _streamTextBlock.Text = "";
        }

        // The window may have been hidden while searching.
        if (!IsVisible)
        {
            _isGenerating = false;
            UpdateSendButton();
            return;
        }

        // Build the request messages: optional system prompt + search context.
        var history = new List<ChatMessage>();
        var systemPrompt = BuildSystemPrompt(ModeCombo.SelectedIndex);
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            history.Add(new ChatMessage("system", systemPrompt));
        }
        if (!string.IsNullOrEmpty(searchContext))
        {
            history.Add(new ChatMessage("system", searchContext));
        }
        history.AddRange(_messages);

        var token = _cts.Token;
        // Zhida accepts role/content context, but some builds reject a "system"
        // role, so fold any system prompt/context into a leading user turn.
        var snapshot = isZhihu
            ? history.Select(m => m.Role == "system" ? new ChatMessage("user", m.Content) : m).ToList()
            : new List<ChatMessage>(history);
        var baseUrl = provider.BaseUrl;
        var apiKey = provider.ApiKey;
        var zhihuSecret = settings.ZhihuAccessSecret;
        // Read UI-owned state on the UI thread before kicking off the background
        // stream, so the background task never touches a WPF control directly.
        bool useThinking = TempThinkingCheck.IsChecked == true;
        var temperature = settings.Temperature;
        var thinkingBudget = settings.ThinkingBudgetTokens;
        var effort = GetEffort(thinkingBudget);
        _ = Task.Run(async () =>
        {
            try
            {
                if (isZhihu)
                {
                    await foreach (var delta in _chat.StreamZhihuAsync(zhihuSecret, model, snapshot, token))
                    {
                        var d = delta;
                        Dispatcher.Invoke(() => AppendStreamChunk(d));
                    }
                }
                else
                {
                    await foreach (var delta in _chat.StreamChatAsync(
                        baseUrl, apiKey, model, temperature,
                        useThinking, thinkingBudget, effort,
                        snapshot, token))
                    {
                        var d = delta;
                        Dispatcher.Invoke(() => AppendStreamChunk(d));
                    }
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

    /// <summary>
    /// Heuristic used by the "自动" search mode: search for short factual
    /// questions and for anything time-sensitive (news, dates, prices...).
    /// </summary>
    private static bool ShouldSearchAuto(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        if (text.Length <= 60
            && Regex.IsMatch(text, @"是什么|什么是|谁|多少|哪个|为什么|怎么回事|哪里|where|who|what|when|how", RegexOptions.IgnoreCase))
        {
            return true;
        }
        return Regex.IsMatch(text, @"最新|新闻|今天|今日|今年|最近|近期|实时|当前|目前|公告|发布|上市|价格|版本|更新|新增|202[4-9]", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Appends the search sources under the answer bubble so the user can
    /// verify the information, plus a one-click link to open Google for the
    /// same query.
    /// </summary>
    private static void AppendSearchSources(StackPanel panel, List<SearchResult> results, string? query, string? providerCode)
    {
        var sources = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var header = new TextBlock
        {
            Text = "📎 信源",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = ReasoningBrush,
            Margin = new Thickness(0, 0, 0, 4)
        };
        sources.Children.Add(header);

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var url = r.Url;
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 12
            };
            var link = new Hyperlink(new Run($"[{i + 1}] {r.Title}"))
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                TextDecorations = new TextDecorationCollection()
            };
            link.Click += (_, _) => OpenUrl(url);
            tb.Inlines.Add(link);
            sources.Children.Add(tb);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var verifyBtn = new Button
            {
                Content = "🔍 用浏览器搜索核验",
                FontSize = 12,
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
                Cursor = Cursors.Hand
            };
            verifyBtn.Click += (_, _) => OpenUrl(BuildVerifyUrl(providerCode, query));
            sources.Children.Add(verifyBtn);
        }

        panel.Children.Add(sources);
    }

    /// <summary>Opens the core query on the matching source site for verification.</summary>
    private static string BuildVerifyUrl(string? providerCode, string query)
    {
        var q = Uri.EscapeDataString(query);
        return providerCode == "Zhihu"
            ? "https://www.zhihu.com/search?type=content&q=" + q
            : "https://www.google.com/search?q=" + q;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore failures to open the browser.
        }
    }

    private static string? GetEffort(int budgetTokens)
    {
        return budgetTokens switch
        {
            > 6000 => "high",
            > 3000 => "medium",
            > 0 => "low",
            _ => null
        };
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

        // Stream is over: swap the plain-text block for a fully rendered
        // Markdown view (headings, lists, tables and highlighted code).
        ReplaceStreamTextWithMarkdown();

        // Show the search sources under the answer for verification.
        if (_lastSearchResults is { Count: > 0 } && _streamPanel != null)
        {
            AppendSearchSources(_streamPanel, _lastSearchResults, _lastQuery, _lastSearchProvider);
            ScrollToBottom();
        }
        _lastSearchResults = null;
        _lastQuery = null;
        _lastSearchProvider = null;

        _streamBuffer = null;
        _streamReasoning = null;
        _streamTextBlock = null;
        _streamReasoningBlock = null;
        _streamPanel = null;
        _isGenerating = false;
        UpdateSendButton();
    }

    /// <summary>
    /// Replaces the plain streaming TextBlock with a Markdown-rendered viewer
    /// once generation has finished. Does nothing when the window was already
    /// closed (the bubble is gone) or the answer is empty.
    /// </summary>
    private void ReplaceStreamTextWithMarkdown()
    {
        if (_streamTextBlock == null || _streamPanel == null)
        {
            return;
        }

        var text = _streamTextBlock.Text;
        if (string.IsNullOrWhiteSpace(text) || !_streamPanel.Children.Contains(_streamTextBlock))
        {
            return;
        }

        int idx = _streamPanel.Children.IndexOf(_streamTextBlock);
        _streamPanel.Children.RemoveAt(idx);

        var viewer = MarkdownRenderService.CreateViewer(text);
        _streamPanel.Children.Insert(idx, viewer);
        ScrollToBottom();
    }

    /// <summary>Formats search hits as a system-prompt block for the model.</summary>
    private static string BuildSearchContext(List<SearchResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("以下是针对用户问题的联网搜索结果，请优先参考这些资料回答，回答末尾可附上相关链接：");
        sb.AppendLine();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.Append('[').Append(i + 1).Append("] ").AppendLine(r.Title);
            if (!string.IsNullOrEmpty(r.Snippet))
            {
                sb.AppendLine(r.Snippet);
            }
            if (!string.IsNullOrEmpty(r.Url))
            {
                sb.AppendLine(r.Url);
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
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
        // AI replies are rendered as Markdown (headings, lists, code blocks
        // with syntax highlighting); user bubbles stay plain text.
        FrameworkElement body = role == "assistant"
            ? MarkdownRenderService.CreateViewer(content)
            : new TextBlock
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White
            };
        if (body is TextBlock userText)
        {
            ApplySharpText(userText);
        }
        var border = new Border
        {
            Background = role == "user" ? UserBubbleBrush : AiBubbleBrush,
            BorderBrush = role == "user" ? UserBubbleBorderBrush : AiBubbleBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 4),
            MaxWidth = 380,
            HorizontalAlignment = role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = body
        };
        MessagesPanel.Children.Add(border);
        ScrollToBottom();
    }

    /// <summary>
    /// Forces ClearType pixel-aligned text rendering. Without it, text inside
    /// the chat bubbles can fall back to grayscale antialiasing and look soft.
    /// </summary>
    private static void ApplySharpText(TextBlock textBlock)
    {
        TextOptions.SetTextFormattingMode(textBlock, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(textBlock, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(textBlock, TextHintingMode.Fixed);
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

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ShowHistoryWindow();
        }
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
        HideChatWindow();
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

        HideChatWindow();
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
