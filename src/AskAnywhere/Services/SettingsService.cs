using System;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
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

    public string FilePath => _filePath;

    /// <summary>
    /// Applies <paramref name="change"/> to the in-memory settings and writes
    /// them to disk. Returns false (with the error message) when saving fails.
    /// </summary>
    public bool Update(Action<AppSettings> change, out string? error)
    {
        lock (_lock)
        {
            change(_settings);
            return Save(out error);
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
                    s.SearchApiKey = Decrypt(s.SearchApiKey) ?? "";
                    s.GoogleSearchApiKey = Decrypt(s.GoogleSearchApiKey) ?? "";
                    s.ZhihuAccessSecret = Decrypt(s.ZhihuAccessSecret) ?? "";

                    if (s.Modes == null || s.Modes.Count == 0)
                    {
                        s.Modes = new AppSettings().Modes;
                    }

                    // Migrate the legacy single-provider fields into the
                    // multi-provider list when needed.
                    if (s.Providers == null || s.Providers.Count == 0)
                    {
                        var legacy = new ChatProvider
                        {
                            Name = "默认",
                            BaseUrl = string.IsNullOrWhiteSpace(s.BaseUrl)
                                ? "https://api.openai.com/v1"
                                : s.BaseUrl,
                            ApiKey = s.ApiKey,
                            Model = s.Model
                        };
                        s.Providers = new List<ChatProvider> { legacy };
                    }
                    foreach (var p in s.Providers)
                    {
                        p.ApiKey = Decrypt(p.ApiKey) ?? "";
                        if (string.IsNullOrWhiteSpace(p.Name))
                        {
                            p.Name = "默认";
                        }
                        if (string.IsNullOrWhiteSpace(p.BaseUrl))
                        {
                            p.BaseUrl = "https://api.openai.com/v1";
                        }
                        if (string.IsNullOrWhiteSpace(p.Kind))
                        {
                            p.Kind = "OpenAI";
                        }
                    }

                    // Make sure a Zhihu (Zhida) provider is always available so
                    // users can pick the Zhida models even after upgrading from
                    // an older settings file.
                    if (!s.Providers.Any(p => p.Kind == "Zhihu"))
                    {
                        s.Providers.Add(new ChatProvider
                        {
                            Name = s.Providers.Any(p => p.Name == "知乎") ? "知乎直答" : "知乎",
                            Kind = "Zhihu",
                            BaseUrl = "zhihu",
                            Model = "zhida-thinking-1p5"
                        });
                    }

                    if (string.IsNullOrWhiteSpace(s.CurrentProvider)
                        || !s.Providers.Any(p => p.Name == s.CurrentProvider))
                    {
                        s.CurrentProvider = s.Providers[0].Name;
                    }

                    // Migrate the old boolean toggle into the new search mode.
                    if (s.SearchMode == null)
                    {
                        s.SearchMode = s.SearchEnabled ? "Always" : "Auto";
                    }

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

    private bool Save(out string? error)
    {
        error = null;
        try
        {
            // Deep copy so encrypted keys are written to disk while the
            // in-memory settings keep the plaintext values.
            var copy = new AppSettings
            {
                BaseUrl = _settings.BaseUrl,
                ApiKey = Encrypt(_settings.ApiKey) ?? "",
                Model = _settings.Model,
                Providers = _settings.Providers?.Select(p => new ChatProvider
                {
                    Name = p.Name,
                    BaseUrl = p.BaseUrl,
                    ApiKey = Encrypt(p.ApiKey) ?? "",
                    Model = p.Model,
                    Kind = p.Kind
                }).ToList() ?? new List<ChatProvider>(),
                CurrentProvider = _settings.CurrentProvider,
                Temperature = _settings.Temperature,
                AutoSendOnSelection = _settings.AutoSendOnSelection,
                AutoHideOnDeactivate = _settings.AutoHideOnDeactivate,
                HotkeyKey = _settings.HotkeyKey,
                HotkeyIntervalMs = _settings.HotkeyIntervalMs,
                AutoStart = _settings.AutoStart,
                Modes = _settings.Modes,
                ThinkingEnabled = _settings.ThinkingEnabled,
                ThinkingBudgetTokens = _settings.ThinkingBudgetTokens,
                SearchEnabled = _settings.SearchEnabled,
                SearchMode = _settings.SearchMode ?? "Auto",
                SearchProvider = _settings.SearchProvider,
                SearchApiKey = Encrypt(_settings.SearchApiKey) ?? "",
                GoogleSearchApiKey = Encrypt(_settings.GoogleSearchApiKey) ?? "",
                CustomSearchUrl = _settings.CustomSearchUrl,
                ZhihuAccessSecret = Encrypt(_settings.ZhihuAccessSecret) ?? "",
                ZhihuSearchCount = _settings.ZhihuSearchCount
            };
            var json = JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Returns the currently selected provider (falls back to the first).</summary>
    public ChatProvider CurrentProvider()
    {
        var providers = _settings.Providers ?? new List<ChatProvider>();
        if (providers.Count == 0)
        {
            providers.Add(new ChatProvider());
        }
        return providers.FirstOrDefault(p => p.Name == _settings.CurrentProvider)
            ?? providers[0];
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
