using System;
using System.Windows;
using AskAnywhere;
using AskAnywhere.Services;

namespace AskAnywhere.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        var s = SettingsService.Instance.Current;
        BaseUrlBox.Text = s.BaseUrl;
        ApiKeyBox.Password = s.ApiKey;
        ModelBox.Text = s.Model;
        TempSlider.Value = s.Temperature;
        TempValue.Text = s.Temperature.ToString("0.0");
        AutoSendCheck.IsChecked = s.AutoSendOnSelection;
        AutoHideCheck.IsChecked = s.AutoHideOnDeactivate;
        AutoStartCheck.IsChecked = s.AutoStart;
        ThresholdSlider.Value = s.DoubleCtrlThresholdMs;
        ThresholdValue.Text = s.DoubleCtrlThresholdMs.ToString();
        CustomPromptBox.Text = s.CustomPrompt;
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
        var s = SettingsService.Instance;
        s.Update(settings =>
        {
            settings.BaseUrl = BaseUrlBox.Text.Trim();
            settings.ApiKey = ApiKeyBox.Password.Trim();
            settings.Model = ModelBox.Text.Trim();
            settings.Temperature = Math.Round(TempSlider.Value, 1);
            settings.AutoSendOnSelection = AutoSendCheck.IsChecked == true;
            settings.AutoHideOnDeactivate = AutoHideCheck.IsChecked == true;
            settings.AutoStart = AutoStartCheck.IsChecked == true;
            settings.DoubleCtrlThresholdMs = (int)Math.Round(ThresholdSlider.Value);
            settings.CustomPrompt = CustomPromptBox.Text.Trim();
        });

        if (Application.Current is App app)
        {
            app.ApplySettings();
        }

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
