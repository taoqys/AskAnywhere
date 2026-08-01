using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using AskAnywhere;
using AskAnywhere.Services;

namespace AskAnywhere.Views;

public partial class SettingsWindow : Window
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x1D, 0x8A, 0x4F));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xC0, 0x28, 0x28));

    private bool _closeRequested;

    public SettingsWindow()
    {
        InitializeComponent();

        HotkeyKeyCombo.Items.Add("Shift");
        HotkeyKeyCombo.Items.Add("Ctrl");
        HotkeyKeyCombo.Items.Add("Alt");
        HotkeyKeyCombo.Items.Add("禁用");
        HotkeyKeyCombo.SelectedIndex = 0;

        var s = SettingsService.Instance.Current;
        BaseUrlBox.Text = s.BaseUrl;
        ApiKeyBox.Password = s.ApiKey;
        ModelBox.Text = s.Model;
        TempSlider.Value = s.Temperature;
        TempValue.Text = s.Temperature.ToString("0.0");
        AutoSendCheck.IsChecked = s.AutoSendOnSelection;
        AutoHideCheck.IsChecked = s.AutoHideOnDeactivate;
        AutoStartCheck.IsChecked = s.AutoStart;
        SelectHotkeyKey(s.HotkeyKey);
        ThresholdSlider.Value = s.HotkeyIntervalMs;
        ThresholdValue.Text = s.HotkeyIntervalMs.ToString();
        CustomPromptBox.Text = s.CustomPrompt;
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
            settings.Model = ModelBox.Text.Trim();
            settings.Temperature = Math.Round(TempSlider.Value, 1);
            settings.AutoSendOnSelection = AutoSendCheck.IsChecked == true;
            settings.AutoHideOnDeactivate = AutoHideCheck.IsChecked == true;
            settings.AutoStart = AutoStartCheck.IsChecked == true;
            settings.HotkeyKey = GetHotkeyKey();
            settings.HotkeyIntervalMs = (int)Math.Round(ThresholdSlider.Value);
            settings.CustomPrompt = CustomPromptBox.Text.Trim();
        }, out string? error);

        if (Application.Current is App app)
        {
            app.ApplySettings();
        }

        if (ok)
        {
            SaveStatusText.Text = "✓ 已保存到 " + SettingsService.Instance.FilePath;
            SaveStatusText.Foreground = SuccessBrush;
        }
        else
        {
            SaveStatusText.Text = "✗ 保存失败: " + (error ?? "未知错误");
            SaveStatusText.Foreground = ErrorBrush;
        }
        return ok;
    }
}
