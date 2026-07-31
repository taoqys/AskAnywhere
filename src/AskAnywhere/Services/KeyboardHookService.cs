using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AskAnywhere.Services;

/// <summary>
/// Detects a quick double-press of Ctrl using a low-level keyboard hook.
/// A "tap" counts only when Ctrl is pressed and released without any other key
/// being pressed in between (so Ctrl+C / Ctrl+V combinations never trigger it).
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint VK_LCONTROL = 0xA2;
    private const uint VK_RCONTROL = 0xA3;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private readonly LowLevelKeyboardProc _proc;
    private readonly object _lock = new();
    private IntPtr _hookHandle = IntPtr.Zero;

    private bool _ctrlDown;
    private long _ctrlDownTicks;
    private bool _otherKeyDuringCtrlDown;
    private long _lastTapDownTicks;
    private bool _doubleCandidate;

    public int ThresholdMs { get; set; }

    public event Action? DoubleCtrlPressed;

    public KeyboardHookService(int thresholdMs)
    {
        ThresholdMs = thresholdMs;
        _proc = HookCallback;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_hookHandle != IntPtr.Zero)
            {
                return;
            }

            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hookHandle == IntPtr.Zero)
            {
                // The hook failed (rare); double-Ctrl will simply be unavailable.
                _hookHandle = IntPtr.Zero;
            }
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    bool isCtrl = data.vkCode == VK_LCONTROL || data.vkCode == VK_RCONTROL;

                    if (isCtrl)
                    {
                        bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                        if (isDown)
                        {
                            if (!_ctrlDown)
                            {
                                _ctrlDown = true;
                                _ctrlDownTicks = Stopwatch.GetTimestamp();
                                _otherKeyDuringCtrlDown = false;

                                if (_lastTapDownTicks != 0 && ElapsedMs(_lastTapDownTicks, _ctrlDownTicks) <= ThresholdMs)
                                {
                                    _doubleCandidate = true;
                                }
                                else
                                {
                                    _doubleCandidate = false;
                                }
                            }
                        }
                        else
                        {
                            if (_ctrlDown)
                            {
                                _ctrlDown = false;
                                if (_otherKeyDuringCtrlDown)
                                {
                                    // This was part of a shortcut like Ctrl+C; not a tap.
                                    _lastTapDownTicks = 0;
                                    _doubleCandidate = false;
                                }
                                else if (_doubleCandidate)
                                {
                                    _lastTapDownTicks = 0;
                                    _doubleCandidate = false;
                                    DoubleCtrlPressed?.Invoke();
                                }
                                else
                                {
                                    _lastTapDownTicks = _ctrlDownTicks;
                                    _doubleCandidate = false;
                                }
                            }
                        }
                    }
                    else if (_ctrlDown && (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN))
                    {
                        _otherKeyDuringCtrlDown = true;
                    }
                }
            }
        }
        catch
        {
            // Never let an exception escape the hook callback.
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static long ElapsedMs(long fromTicks, long toTicks)
    {
        return (toTicks - fromTicks) * 1000 / Stopwatch.Frequency;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
    }
}
