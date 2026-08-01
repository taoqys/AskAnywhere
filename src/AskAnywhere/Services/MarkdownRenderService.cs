using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MdXaml;

namespace AskAnywhere.Services;

/// <summary>
/// Renders AI reply text as a light-theme Markdown FlowDocument with
/// AvalonEdit syntax highlighting (via MdXaml).
/// </summary>
public static class MarkdownRenderService
{
    // One shared engine: Transform() is called on the UI thread only, so a
    // single instance keeps things light (no per-message overhead).
    private static readonly Markdown Engine = CreateEngine();

    private static Markdown CreateEngine()
    {
        var engine = new Markdown
        {
            DocumentStyle = (Style)Application.Current.FindResource("AskMarkdownStyle"),
            DisabledTootip = true,
            DisabledLazyLoad = true,
            DisabledContextMenu = true,
            OnHyperLinkClicked = url =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch
                {
                    // Never crash the app because a link could not be opened.
                }
            }
        };
        return engine;
    }

    /// <summary>
    /// Builds a read-only RichTextBox that hosts the rendered Markdown. The
    /// viewer grows with its content (scrollbar disabled) so it can live inside
    /// a chat bubble inside the outer ScrollViewer.
    /// </summary>
    public static RichTextBox CreateViewer(string markdown)
    {
        var viewer = new RichTextBox
        {
            IsReadOnly = true,
            IsDocumentEnabled = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsInactiveSelectionHighlightEnabled = true,
            FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"),
            FontSize = 13
        };
        ApplySharpText(viewer);

        try
        {
            viewer.Document = Engine.Transform(markdown ?? "");
        }
        catch
        {
            // Fall back to a plain-text document if the renderer chokes on
            // unusual input, so the reply is never lost.
            var doc = new System.Windows.Documents.FlowDocument(
                new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(markdown ?? "")));
            doc.FontFamily = viewer.FontFamily;
            doc.FontSize = viewer.FontSize;
            doc.Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
            ApplySharpText(doc);
            viewer.Document = doc;
        }

        return viewer;
    }

    /// <summary>
    /// Enables ClearType pixel-aligned rendering so the Markdown text is as
    /// crisp as the rest of the UI.
    /// </summary>
    private static void ApplySharpText(System.Windows.Documents.FlowDocument document)
    {
        TextOptions.SetTextFormattingMode(document, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(document, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(document, TextHintingMode.Fixed);
    }

    /// <summary>
    /// Enables ClearType pixel-aligned rendering on the viewer control itself.
    /// </summary>
    private static void ApplySharpText(RichTextBox viewer)
    {
        TextOptions.SetTextFormattingMode(viewer, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(viewer, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(viewer, TextHintingMode.Fixed);
    }
}
