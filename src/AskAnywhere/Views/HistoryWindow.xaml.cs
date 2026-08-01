using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AskAnywhere.Models;
using AskAnywhere.Services;

namespace AskAnywhere.Views;

public partial class HistoryWindow : Window
{
    private static readonly SolidColorBrush UserHeaderBrush = new(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly SolidColorBrush AiHeaderBrush = new(Color.FromRgb(0x5A, 0x5A, 0x5A));
    private static readonly SolidColorBrush BodyBrush = new(Color.FromRgb(0x1F, 0x1F, 0x1F));

    private List<ChatSession> _sessions = new();

    public HistoryWindow()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        _sessions = HistoryService.LoadAll();
        SessionList.Items.Clear();

        // Newest first.
        for (int i = _sessions.Count - 1; i >= 0; i--)
        {
            var s = _sessions[i];
            string summary = s.Messages.Count > 0 ? s.Messages[0].Content : "";
            if (summary.Length > 28)
            {
                summary = summary.Substring(0, 28) + "…";
            }
            SessionList.Items.Add(new ListBoxItem
            {
                Content = s.CreatedAt.ToString("MM-dd HH:mm") + "  " + summary,
                Tag = s
            });
        }

        DetailPanel.Children.Clear();
    }

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailPanel.Children.Clear();
        if (SessionList.SelectedItem is ListBoxItem li && li.Tag is ChatSession s)
        {
            foreach (var msg in s.Messages)
            {
                DetailPanel.Children.Add(new TextBlock
                {
                    Text = msg.Role == "user" ? "你" : "AI",
                    FontWeight = FontWeights.Bold,
                    Foreground = msg.Role == "user" ? UserHeaderBrush : AiHeaderBrush,
                    Margin = new Thickness(0, 10, 0, 3)
                });
                DetailPanel.Children.Add(new TextBlock
                {
                    Text = msg.Content,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = BodyBrush
                });
            }
        }
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryService.ClearAll();
        Reload();
    }
}
