using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace AskAnywhere.Services;

/// <summary>
/// Captures the currently selected text from the focused application.
///
/// Strategy:
///   1. UI Automation first (no clipboard side effects).
///   2. Ctrl+C clipboard fallback for apps UIA cannot read.
///
/// Safety: the clipboard fallback is skipped on terminal-like windows
/// (PowerShell / cmd / Windows Terminal / mintty etc.) because there Ctrl+C
/// may interrupt a running command instead of copying. The captured text is
/// only accepted when the clipboard content actually changed, and the user's
/// previous clipboard is restored afterwards.
///
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

        // 2) Clipboard fallback (simulate Ctrl+C). Never on terminals: sending
        //    Ctrl+C there can abort a running command even when nothing is selected.
        if (IsTerminalLikeWindow())
        {
            return null;
        }

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
                // Only accept the result when the clipboard actually changed;
                // otherwise nothing was copied (e.g. nothing selected).
                if (!string.IsNullOrEmpty(result) && result != originalText)
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
        if (originalText != null && !string.IsNullOrEmpty(captured) && captured != originalText)
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

    /// <summary>
    /// Detects terminal / command-line windows where simulating Ctrl+C is risky.
    /// </summary>
    private static bool IsTerminalLikeWindow()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            string cls = className.ToString();
            if (cls == "ConsoleWindowClass" || cls == "CASCADIA_HOSTING_WINDOW_CLASS")
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            using var proc = Process.GetProcessById((int)pid);
            string name = proc.ProcessName.ToLowerInvariant();
            return name is "powershell" or "pwsh" or "cmd" or "windowsterminal"
                or "conhost" or "mintty" or "winpty-agent";
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
