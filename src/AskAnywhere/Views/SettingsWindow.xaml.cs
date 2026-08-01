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
        BaseUrlBox.Text = s.BaseUrl;
        ApiKeyBox.Password = s.ApiKey;
        ModelCombo.Text = s.Model;
        TempSlider.Value = s.Temperature;
        TempValue.Text = s.Temperature.ToString("0.0");
        ThinkingCheck.IsChecked = s.ThinkingEnabled;
        SelectThinkingBudget(s.ThinkingBudgetTokens);
        AutoSendCheck.IsChecked = s.AutoSendOnSelection;
        AutoHideCheck.IsChecked = s.AutoHideOnDeactivate;
        AutoStartCheck.IsChecked = s.AutoStart;
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
        var baseUrl = BaseUrlBox.Text.Trim();
        var apiKey = ApiKeyBox.Password.Trim();
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
        {
            ShowStatus("请先填写 Base URL 和 API Key", false);
            return;
        }

        try
        {
            ModelFetchButton.IsEnabled = false;
            ModelFetchButton.Content = "获取中…";
            var models = await _chat.GetModelsAsync(baseUrl, apiKey, CancellationToken.None);

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
            ModesList.Items[idx] = m.Name;
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
            settings.BaseUrl = BaseUrlBox.Text.Trim();
            settings.ApiKey = ApiKeyBox.Password.Trim();
            settings.Model = ModelCombo.Text.Trim();
            settings.Temperature = Math.Round(TempSlider.Value, 1);
            settings.ThinkingEnabled = ThinkingCheck.IsChecked == true;
            settings.ThinkingBudgetTokens = GetThinkingBudget();
            settings.AutoSendOnSelection = AutoSendCheck.IsChecked == true;
            settings.AutoHideOnDeactivate = AutoHideCheck.IsChecked == true;
            settings.AutoStart = AutoStartCheck.IsChecked == true;
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
