using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AskAnywhere.Models;

namespace AskAnywhere.Services;

public sealed class SettingsService
{
    private static readonly Lazy<SettingsService> LazyInstance = new(() => new SettingsService());
    public static SettingsService Instance => LazyInstance.Value;

    private readonly string _filePath;
    private AppSettings _settings;
    private readonly object _lock = new();

    private SettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AskAnywhere");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
        _settings = Load();
    }

    public AppSettings Current
    {
        get { lock (_lock) return _settings; }
    }

    public void Update(Action<AppSettings> change)
    {
        lock (_lock)
        {
            change(_settings);
            Save();
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null)
                {
                    s.ApiKey = Decrypt(s.ApiKey) ?? "";
                    return s;
                }
            }
        }
        catch
        {
            // Fall back to defaults on any read error.
        }
        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            var copy = new AppSettings
            {
                BaseUrl = _settings.BaseUrl,
                ApiKey = Encrypt(_settings.ApiKey) ?? "",
                Model = _settings.Model,
                Temperature = _settings.Temperature,
                AutoSendOnSelection = _settings.AutoSendOnSelection,
                AutoHideOnDeactivate = _settings.AutoHideOnDeactivate,
                DoubleCtrlThresholdMs = _settings.DoubleCtrlThresholdMs,
                AutoStart = _settings.AutoStart,
                CustomPrompt = _settings.CustomPrompt
            };
            var json = JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Ignore save failures so the app keeps running.
        }
    }

    private static string? Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return null;
        }
        try
        {
            var data = Encoding.UTF8.GetBytes(plain);
            var enc = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }
        catch
        {
            return plain;
        }
    }

    private static string? Decrypt(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher))
        {
            return null;
        }
        try
        {
            var data = Convert.FromBase64String(cipher);
            var dec = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        catch
        {
            return cipher;
        }
    }
}
