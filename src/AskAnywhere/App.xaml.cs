using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AskAnywhere.Services;
using AskAnywhere.Views;

namespace AskAnywhere;

public partial class App : Application
{
    private const string MutexName = @"Local\AskAnywhere.SingleInstance";
    private const string ActivateEventName = @"Local\AskAnywhere.Activate";

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;

    private KeyboardHookService? _keyboardHook;
    private TrayIconService? _tray;
    private ChatWindow? _chatWindow;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check: if another instance is running, ask it to show
        // the window and exit immediately.
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                using var evt = EventWaitHandle.OpenExisting(ActivateEventName);
                evt.Set();
            }
            catch
            {
                // The other instance may not be listening yet; ignore.
            }
            Shutdown();
            return;
        }

        try
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        }
        catch
        {
            _activateEvent = null;
        }

        if (_activateEvent != null)
        {
            var watcher = new Thread(WatchActivateEvent)
            {
                IsBackground = true,
                Name = "AskAnywhere.ActivateWatcher"
            };
            watcher.Start();
        }

        var settings = SettingsService.Instance.Current;

        _tray = new TrayIconService();
        _tray.Show();
        _tray.DoubleClicked += ToggleChatWindow;
        _tray.OpenRequested += ShowChatWindow;
        _tray.SettingsRequested += ShowSettingsWindow;
        _tray.HistoryRequested += ShowHistoryWindow;
        _tray.ExitRequested += ShutdownApp;

        _keyboardHook = new KeyboardHookService(ParseHotkeyKey(settings.HotkeyKey), settings.HotkeyIntervalMs);
        _keyboardHook.DoubleTapPressed += ToggleChatWindow;
        ApplyHotkey();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _chatWindow?.SaveCurrentSession();
        _keyboardHook?.Dispose();
        _tray?.Dispose();

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // Ignore release failures during shutdown.
        }

        base.OnExit(e);
    }

    private void WatchActivateEvent()
    {
        if (_activateEvent == null)
        {
            return;
        }

        while (true)
        {
            try
            {
                _activateEvent.WaitOne();
            }
            catch
            {
                break;
            }

            try
            {
                Dispatcher.Invoke(() => ShowChatWindow());
            }
            catch
            {
                break;
            }
        }
    }

    private void ToggleChatWindow()
    {
        if (_chatWindow == null)
        {
            ShowChatWindow();
            return;
        }

        if (_chatWindow.IsVisible && _chatWindow.IsActive)
        {
            _chatWindow.HideChatWindow();
        }
        else
        {
            ShowChatWindow();
        }
    }

    public void ShowChatWindow()
    {
        if (_chatWindow == null)
        {
            _chatWindow = new ChatWindow();
            _chatWindow.Closed += (_, _) => _chatWindow = null;
        }

        if (!_chatWindow.IsVisible)
        {
            // Show without stealing focus first so selected text can be captured
            // from the app that currently has focus.
            _chatWindow.ShowActivated = false;
            _chatWindow.Show();
        }

        if (_chatWindow.WindowState == WindowState.Minimized)
        {
            _chatWindow.WindowState = WindowState.Normal;
        }

        _chatWindow.Topmost = true;
        _chatWindow.PrepareForShow();
    }

    public void ShowSettingsWindow()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowHistoryWindow()
    {
        if (_historyWindow == null)
        {
            _historyWindow = new HistoryWindow();
            _historyWindow.Closed += (_, _) => _historyWindow = null;
        }

        _historyWindow.Show();
        _historyWindow.Activate();
    }

    public void ApplySettings()
    {
        var s = SettingsService.Instance.Current;
        ApplyHotkey();
        AutoStartService.SetEnabled(s.AutoStart);
    }

    private void ApplyHotkey()
    {
        var s = SettingsService.Instance.Current;
        if (_keyboardHook == null)
        {
            return;
        }

        _keyboardHook.SetKey(ParseHotkeyKey(s.HotkeyKey));
        _keyboardHook.ThresholdMs = s.HotkeyIntervalMs;

        if (ParseHotkeyKey(s.HotkeyKey) == HotkeyKey.Disabled)
        {
            _keyboardHook.Stop();
        }
        else
        {
            _keyboardHook.Start();
        }
    }

    private static HotkeyKey ParseHotkeyKey(string? value)
    {
        return value?.Trim() switch
        {
            "Ctrl" => HotkeyKey.Ctrl,
            "Alt" => HotkeyKey.Alt,
            "Disabled" => HotkeyKey.Disabled,
            _ => HotkeyKey.Shift
        };
    }

    private void ShutdownApp()
    {
        Shutdown();
    }
}
