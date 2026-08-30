using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using AskAnywhere;
using AskAnywhere.Models;
using AskAnywhere.Services;

namespace AskAnywhere.Views;

public partial class SettingsWindow : Window
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x1D, 0x8A, 0x4F));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xC0, 0x28, 0x28));

    private readonly ChatService _chat = new();
    private bool _closeRequested;
    private bool _updatingModeUi;

    public SettingsWindow()
    {
        InitializeComponent();

        HotkeyKeyCombo.Items.Add("Shift");
        HotkeyKeyCombo.Items.Add("Ctrl");
        HotkeyKeyCombo.Items.Add("Alt");
        HotkeyKeyCombo.Items.Add("禁用");
        HotkeyKeyCombo.SelectedIndex = 0;

        ThinkingBudgetCombo.Items.Add("自动");
        ThinkingBudgetCombo.Items.Add("低（约 2k）");
        ThinkingBudgetCombo.Items.Add("中（约 4k）");
        ThinkingBudgetCombo.Items.Add("高（约 8k）");
        ThinkingBudgetCombo.SelectedIndex = 0;

        var s = SettingsService.Instance.Current;
        ReloadProviders();
        TempSlider.Value = s.Temperature;
        TempValue.Text = s.Temperature.ToString("0.0");
        ThinkingCheck.IsChecked = s.ThinkingEnabled;
        SelectThinkingBudget(s.ThinkingBudgetTokens);
        AutoSendCheck.IsChecked = s.AutoSendOnSelection;
        AutoHideCheck.IsChecked = s.AutoHideOnDeactivate;
        AutoStartCheck.IsChecked = s.AutoStart;

        SearchModeCombo.Items.Add("自动");
        SearchModeCombo.Items.Add("始终");
        SearchModeCombo.Items.Add("关闭");
        SelectSearchMode(s.SearchMode);

        SearchDecisionCombo.Items.Add("模型判定");
        SearchDecisionCombo.Items.Add("关键词");
        SelectSearchDecision(s.AutoSearchDecision);

        SearchProviderCombo.Items.Add("Tavily");
        SearchProviderCombo.Items.Add("Google");
        SearchProviderCombo.Items.Add("知乎");
        SearchProviderCombo.Items.Add("自定义");
        SelectSearchProvider(s.SearchProvider);
        SearchApiKeyBox.Password = s.SearchApiKey;
        GoogleSearchApiKeyBox.Password = s.GoogleSearchApiKey;
        CustomSearchUrlBox.Text = s.CustomSearchUrl;
        ZhihuAccessSecretBox.Password = s.ZhihuAccessSecret;
        UpdateSearchPanels();

        SelectHotkeyKey(s.HotkeyKey);
        ThresholdSlider.Value = s.HotkeyIntervalMs;
        ThresholdValue.Text = s.HotkeyIntervalMs.ToString();
        LoadModes();
    }

    private void SelectHotkeyKey(string? key)
    {
        HotkeyKeyCombo.SelectedIndex = key?.Trim() switch
        {
            "Ctrl" => 1,
            "Alt" => 2,
            "Disabled" => 3,
            _ => 0
        };
    }

    private string GetHotkeyKey()
    {
        return HotkeyKeyCombo.SelectedIndex switch
        {
            1 => "Ctrl",
            2 => "Alt",
            3 => "Disabled",
            _ => "Shift"
        };
    }

    private void SelectThinkingBudget(int tokens)
    {
        ThinkingBudgetCombo.SelectedIndex = tokens switch
        {
            > 6000 => 3,
            > 3000 => 2,
            > 0 => 1,
            _ => 0
        };
    }

    private int GetThinkingBudget()
    {
        return ThinkingBudgetCombo.SelectedIndex switch
        {
            1 => 2048,
            2 => 4096,
            3 => 8192,
            _ => 0
        };
    }

    private bool _updatingProviderUi;

    private void ReloadProviders()
    {
        _updatingProviderUi = true;
        var providers = SettingsService.Instance.Current.Providers;
        ProviderCombo.Items.Clear();
        foreach (var p in providers)
        {
            ProviderCombo.Items.Add(p.Name);
        }
        var current = SettingsService.Instance.Current.CurrentProvider;
        int idx = providers.FindIndex(p => p.Name == current);
        ProviderCombo.SelectedIndex = idx >= 0 ? idx : 0;
        _updatingProviderUi = false;
        ShowSelectedProvider();
    }

    private ChatProvider? SelectedProvider()
    {
        var providers = SettingsService.Instance.Current.Providers;
        int idx = ProviderCombo.SelectedIndex;
        return idx >= 0 && idx < providers.Count ? providers[idx] : null;
    }

    private void ProviderCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingProviderUi)
        {
            return;
        }
        ShowSelectedProvider();
    }

    private void ShowSelectedProvider()
    {
        _updatingProviderUi = true;
        try
        {
            var p = SelectedProvider();
            ProviderNameBox.Text = p?.Name ?? "";
            bool isZhihu = p?.Kind == "Zhihu";

            ProviderBaseUrlPanel.Visibility = isZhihu ? Visibility.Collapsed : Visibility.Visible;
            ProviderApiKeyPanel.Visibility = isZhihu ? Visibility.Collapsed : Visibility.Visible;
            ProviderZhihuNote.Visibility = isZhihu ? Visibility.Visible : Visibility.Collapsed;

            BaseUrlBox.Text = p?.BaseUrl ?? "";
            ApiKeyBox.Password = p?.ApiKey ?? "";

            if (isZhihu)
            {
                ModelCombo.Items.Clear();
                foreach (var m in ChatService.ZhihuModels)
                {
                    ModelCombo.Items.Add(m);
                }
                ModelCombo.Text = string.IsNullOrWhiteSpace(p?.Model) ? "zhida-thinking-1p5" : p!.Model;
            }
            else
            {
                ModelCombo.Text = p?.Model ?? "";
            }
        }
        finally
        {
            _updatingProviderUi = false;
        }
    }

    private void ProviderNameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updatingProviderUi)
        {
            return;
        }
        var p = SelectedProvider();
        if (p == null)
        {
            return;
        }
        p.Name = ProviderNameBox.Text;
        int idx = ProviderCombo.SelectedIndex;
        if (idx >= 0 && idx < ProviderCombo.Items.Count)
        {
            // Guard the item swap so it cannot re-enter the selection-changed
            // handler and reset the editor while the user is typing.
            _updatingProviderUi = true;
            try
            {
                ProviderCombo.Items[idx] = p.Name;
            }
            finally
            {
                _updatingProviderUi = false;
            }
        }
    }

    private void AddProviderButton_Click(object sender, RoutedEventArgs e)
    {
        var providers = SettingsService.Instance.Current.Providers;
        var p = new ChatProvider { Name = "新供应商" + (providers.Count + 1) };
        providers.Add(p);
        ReloadProviders();
        ProviderCombo.SelectedIndex = providers.Count - 1;
        ProviderNameBox.Focus();
        ProviderNameBox.SelectAll();
    }

    private void RemoveProviderButton_Click(object sender, RoutedEventArgs e)
    {
        var providers = SettingsService.Instance.Current.Providers;
        if (providers.Count <= 1)
        {
            ShowStatus("至少保留一个供应商", false);
            return;
        }
        int idx = ProviderCombo.SelectedIndex;
        if (idx < 0)
        {
            return;
        }
        providers.RemoveAt(idx);
        ReloadProviders();
        ProviderCombo.SelectedIndex = Math.Min(idx, providers.Count - 1);
    }

    private void SetDefaultProviderButton_Click(object sender, RoutedEventArgs e)
    {
        var p = SelectedProvider();
        if (p == null)
        {
            return;
        }
        SettingsService.Instance.Update(settings => settings.CurrentProvider = p.Name, out _);
        ShowStatus("已设为默认供应商", true);
    }

    private void SelectSearchMode(string? mode)
    {
        SearchModeCombo.SelectedIndex = mode?.Trim().ToLowerInvariant() switch
        {
            "always" => 1,
            "off" => 2,
            _ => 0
        };
    }

    private string GetSearchMode()
    {
        return SearchModeCombo.SelectedIndex switch
        {
            1 => "Always",
            2 => "Off",
            _ => "Auto"
        };
    }

    private void SelectSearchDecision(string? mode)
    {
        SearchDecisionCombo.SelectedIndex = mode?.Trim().ToLowerInvariant() == "heuristic" ? 1 : 0;
    }

    private string GetSearchDecision()
    {
        return SearchDecisionCombo.SelectedIndex == 1 ? "Heuristic" : "Model";
    }

    private void SelectSearchProvider(string? provider)
    {
        SearchProviderCombo.SelectedIndex = provider?.Trim().ToLowerInvariant() switch
        {
            "google" => 1,
            "zhihu" => 2,
            "custom" => 3,
            _ => 0
        };
    }

    private string GetSearchProvider()
    {
        return SearchProviderCombo.SelectedIndex switch
        {
            1 => "Google",
            2 => "Zhihu",
            3 => "Custom",
            _ => "Tavily"
        };
    }

    private void SearchProviderCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateSearchPanels();
    }

    private void UpdateSearchPanels()
    {
        if (SearchApiKeyPanel == null || CustomSearchUrlPanel == null)
        {
            return;
        }
        int idx = SearchProviderCombo.SelectedIndex;
        bool isCustom = idx == 3;
        bool isGoogle = idx == 1;
        bool isZhihu = idx == 2;

        SearchApiKeyPanel.Visibility = (isCustom || isGoogle || isZhihu)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        GoogleSearchApiKeyPanel.Visibility = isGoogle ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ZhihuSearchProviderNote.Visibility = isZhihu ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        CustomSearchUrlPanel.Visibility = isCustom ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private void TempSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TempValue != null)
        {
            TempValue.Text = e.NewValue.ToString("0.0");
        }
    }

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ThresholdValue != null)
        {
            ThresholdValue.Text = ((int)Math.Round(e.NewValue)).ToString();
        }
    }

    private async void ModelFetchButton_Click(object sender, RoutedEventArgs e)
    {
        var p = SelectedProvider();
        if (p == null)
        {
            ShowStatus("请先选择供应商", false);
            return;
        }

        // Zhihu (Zhida) exposes a fixed model list, no /models endpoint.
        if (p.Kind == "Zhihu")
        {
            ModelFetchButton.IsEnabled = false;
            ModelFetchButton.Content = "获取中…";
            try
            {
                var current = ModelCombo.Text;
                ModelCombo.Items.Clear();
                foreach (var id in ChatService.ZhihuModels)
                {
                    ModelCombo.Items.Add(id);
                }
                ModelCombo.Text = ChatService.ZhihuModels.Contains(current) ? current : ChatService.ZhihuModels[1];
                ShowStatus($"知乎直答模型 {ChatService.ZhihuModels.Length} 个", true);
            }
            finally
            {
                ModelFetchButton.IsEnabled = true;
                ModelFetchButton.Content = "获取模型列表";
            }
            return;
        }

        p.BaseUrl = BaseUrlBox.Text.Trim();
        p.ApiKey = ApiKeyBox.Password.Trim();
        if (string.IsNullOrEmpty(p.BaseUrl) || string.IsNullOrEmpty(p.ApiKey))
        {
            ShowStatus("请先填写 Base URL 和 API Key", false);
            return;
        }

        try
        {
            ModelFetchButton.IsEnabled = false;
            ModelFetchButton.Content = "获取中…";
            var models = await _chat.GetModelsAsync(p.BaseUrl, p.ApiKey, CancellationToken.None);

            var current = ModelCombo.Text;
            ModelCombo.Items.Clear();
            foreach (var id in models)
            {
                ModelCombo.Items.Add(id);
            }
            if (!string.IsNullOrEmpty(current))
            {
                ModelCombo.Text = current;
            }
            else if (models.Count > 0)
            {
                ModelCombo.Text = models[0];
            }

            ShowStatus($"获取到 {models.Count} 个模型，请选择", true);
        }
        catch (Exception ex)
        {
            ShowStatus("获取模型失败: " + ex.Message, false);
        }
        finally
        {
            ModelFetchButton.IsEnabled = true;
            ModelFetchButton.Content = "获取模型列表";
        }
    }

    private void LoadModes()
    {
        _updatingModeUi = true;
        ModesList.Items.Clear();
        foreach (var m in SettingsService.Instance.Current.Modes)
        {
            ModesList.Items.Add(m.Name);
        }
        if (ModesList.Items.Count > 0)
        {
            ModesList.SelectedIndex = 0;
        }
        else
        {
            ModeNameBox.Text = "";
            ModePromptBox.Text = "";
        }
        _updatingModeUi = false;
    }

    private ChatMode? SelectedMode()
    {
        var modes = SettingsService.Instance.Current.Modes;
        int idx = ModesList.SelectedIndex;
        return idx >= 0 && idx < modes.Count ? modes[idx] : null;
    }

    private void ModesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingModeUi)
        {
            return;
        }

        _updatingModeUi = true;
        var m = SelectedMode();
        ModeNameBox.Text = m?.Name ?? "";
        ModePromptBox.Text = m?.Prompt ?? "";
        _updatingModeUi = false;
    }

    private void ModeNameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updatingModeUi)
        {
            return;
        }

        var m = SelectedMode();
        if (m == null)
        {
            return;
        }
        m.Name = ModeNameBox.Text;
        int idx = ModesList.SelectedIndex;
        if (idx >= 0 && idx < ModesList.Items.Count)
        {
            _updatingModeUi = true;
            try
            {
                ModesList.Items[idx] = m.Name;
            }
            finally
            {
                _updatingModeUi = false;
            }
        }
    }

    private void ModePromptBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updatingModeUi)
        {
            return;
        }

        var m = SelectedMode();
        if (m != null)
        {
            m.Prompt = ModePromptBox.Text;
        }
    }

    private void AddModeButton_Click(object sender, RoutedEventArgs e)
    {
        var modes = SettingsService.Instance.Current.Modes;
        var m = new ChatMode { Name = "新功能", Prompt = "" };
        modes.Add(m);
        LoadModes();
        ModesList.SelectedIndex = ModesList.Items.Count - 1;
        ModeNameBox.Focus();
        ModeNameBox.SelectAll();
    }

    private void RemoveModeButton_Click(object sender, RoutedEventArgs e)
    {
        var modes = SettingsService.Instance.Current.Modes;
        int idx = ModesList.SelectedIndex;
        if (idx < 0 || modes.Count <= 1)
        {
            ShowStatus("至少保留一个功能", false);
            return;
        }
        modes.RemoveAt(idx);
        LoadModes();
        if (ModesList.Items.Count > 0)
        {
            ModesList.SelectedIndex = Math.Min(idx, ModesList.Items.Count - 1);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SaveSettings())
        {
            _closeRequested = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _closeRequested = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing via the window X button also saves, so edits are never lost
        // by accident. Only the explicit "取消" button discards changes.
        if (!_closeRequested)
        {
            if (!SaveSettings())
            {
                e.Cancel = true; // Stay open so the user can fix the problem.
            }
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// Writes the current form values to disk. Returns true on success.
    /// </summary>
    private bool SaveSettings()
    {
        var s = SettingsService.Instance;
        bool ok = s.Update(settings =>
        {
            // Write the edited values back to the selected provider.
            var p = SelectedProvider();
            if (p != null)
            {
                var oldName = p.Name;
                p.Name = ProviderNameBox.Text.Trim();
                p.BaseUrl = BaseUrlBox.Text.Trim();
                p.ApiKey = ApiKeyBox.Password.Trim();
                p.Model = ModelCombo.Text.Trim();

                // Keep CurrentProvider in sync when the active provider is renamed.
                if (!string.IsNullOrEmpty(oldName) && settings.CurrentProvider == oldName)
                {
                    settings.CurrentProvider = p.Name;
                }
            }
            if (settings.Providers.Count > 0 && string.IsNullOrEmpty(settings.CurrentProvider))
            {
                settings.CurrentProvider = settings.Providers[0].Name;
            }
            settings.Temperature = Math.Round(TempSlider.Value, 1);
            settings.ThinkingEnabled = ThinkingCheck.IsChecked == true;
            settings.ThinkingBudgetTokens = GetThinkingBudget();
            settings.AutoSendOnSelection = AutoSendCheck.IsChecked == true;
            settings.AutoHideOnDeactivate = AutoHideCheck.IsChecked == true;
            settings.AutoStart = AutoStartCheck.IsChecked == true;
            settings.SearchMode = GetSearchMode();
            settings.SearchEnabled = GetSearchMode() != "Off";
            settings.AutoSearchDecision = GetSearchDecision();
            settings.SearchProvider = GetSearchProvider();
            settings.SearchApiKey = SearchApiKeyBox.Password.Trim();
            settings.GoogleSearchApiKey = GoogleSearchApiKeyBox.Password.Trim();
            settings.CustomSearchUrl = CustomSearchUrlBox.Text.Trim();
            settings.ZhihuAccessSecret = ZhihuAccessSecretBox.Password.Trim();
            settings.HotkeyKey = GetHotkeyKey();
            settings.HotkeyIntervalMs = (int)Math.Round(ThresholdSlider.Value);
            // Modes are edited in place on the current instance.
        }, out string? error);

        if (Application.Current is App app)
        {
            app.ApplySettings();
        }

        if (ok)
        {
            ShowStatus("✓ 已保存到 " + SettingsService.Instance.FilePath, true);
        }
        else
        {
            ShowStatus("✗ 保存失败: " + (error ?? "未知错误"), false);
        }
        return ok;
    }

    private void ShowStatus(string message, bool success)
    {
        SaveStatusText.Text = message;
        SaveStatusText.Foreground = success ? SuccessBrush : ErrorBrush;
    }
}
