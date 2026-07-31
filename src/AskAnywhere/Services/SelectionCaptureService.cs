using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace AskAnywhere.Services;

/// <summary>
/// Captures the currently selected text from the focused application.
/// Strategy: UI Automation first (no clipboard side effects), then a Ctrl+C
/// clipboard fallback for apps UIA cannot read.
/// Must be called from an STA thread for the clipboard path.
/// </summary>
public static class SelectionCaptureService
{
    public static async Task<string?> GetSelectedTextAsync(CancellationToken ct)
    {
        // 1) Try UI Automation on a background thread (fast, non-invasive).
        try
        {
            var uiaTask = Task.Run(() => TryGetSelectionViaUia());
            var text = await uiaTask.WaitAsync(TimeSpan.FromMilliseconds(250), ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }
        catch
        {
            // Fall through to the clipboard approach.
        }

        // 2) Clipboard fallback (simulate Ctrl+C).
        return await TryGetSelectionViaClipboardAsync(ct);
    }

    private static string? TryGetSelectionViaUia()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null)
            {
                return null;
            }

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj) && patternObj is TextPattern tp)
            {
                var ranges = tp.GetSelection();
                if (ranges != null && ranges.Length > 0)
                {
                    var t = ranges[0].GetText(-1);
                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        return t;
                    }
                }
            }

            // Some applications expose the selection on a child element.
            var all = focused.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement el in all)
            {
                try
                {
                    if (el.TryGetCurrentPattern(TextPattern.Pattern, out var p) && p is TextPattern tp2)
                    {
                        var ranges = tp2.GetSelection();
                        if (ranges != null && ranges.Length > 0)
                        {
                            var t = ranges[0].GetText(-1);
                            if (!string.IsNullOrWhiteSpace(t))
                            {
                                return t;
                            }
                        }
                    }
                }
                catch
                {
                    // Keep searching.
                }
            }
        }
        catch
        {
            // Ignore UIA failures.
        }
        return null;
    }

    private static async Task<string?> TryGetSelectionViaClipboardAsync(CancellationToken ct)
    {
        string? originalText = null;
        try
        {
            originalText = Clipboard.GetText();
        }
        catch
        {
            // Clipboard may be busy or contain non-text data; proceed anyway.
        }

        try
        {
            SendKeys.SendWait("^c");
        }
        catch
        {
            return null;
        }

        string? result = null;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 300 && !ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(30, ct);
                result = Clipboard.GetText();
                if (!string.IsNullOrEmpty(result))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Clipboard busy; retry.
            }
        }

        // Restore the user's original clipboard a little later, but only if it
        // still holds the text we captured.
        var captured = result;
        if (originalText != null && !string.IsNullOrEmpty(captured))
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(800);
                try
                {
                    if (Clipboard.GetText() == captured)
                    {
                        Clipboard.SetText(originalText);
                    }
                }
                catch
                {
                    // Ignore restore failures.
                }
            });
        }

        return string.IsNullOrEmpty(result) ? null : result.Trim();
    }
}
