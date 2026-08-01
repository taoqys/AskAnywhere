using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace AskAnywhere.Services;

/// <summary>
/// Captures the currently selected text from the focused application using
/// UI Automation only. Never touches the clipboard and never simulates keys,
/// so there are no side effects when nothing is selected.
/// Must not steal focus before this runs.
/// </summary>
public static class SelectionCaptureService
{
    public static async Task<string?> GetSelectedTextAsync(CancellationToken ct)
    {
        try
        {
            var uiaTask = Task.Run(() => TryGetSelectionViaUia());
            var text = await uiaTask.WaitAsync(TimeSpan.FromMilliseconds(250), ct);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
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
}
