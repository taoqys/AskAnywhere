using System.Collections.Generic;
using System.Linq;

namespace AskAnywhere.Models;

public sealed class AppSettings
{
    /// <summary>Legacy single-provider fields, kept for migration only.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>All configured chat providers; the app uses the current one.</summary>
    public List<ChatProvider> Providers { get; set; } = new()
    {
        new() { Name = "默认", BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o-mini" },
        new() { Name = "知乎", Kind = "Zhihu", BaseUrl = "zhihu", Model = "zhida-thinking-1p5" }
    };

    /// <summary>Name of the provider selected in the chat window.</summary>
    public string CurrentProvider { get; set; } = "默认";

    public double Temperature { get; set; } = 0.7;
    public bool AutoSendOnSelection { get; set; } = false;
    public bool AutoHideOnDeactivate { get; set; } = true;

    /// <summary>Trigger key for the double-tap hotkey: Disabled | Ctrl | Shift | Alt.</summary>
    public string HotkeyKey { get; set; } = "Shift";

    /// <summary>Max interval between the two taps, in milliseconds.</summary>
    public int HotkeyIntervalMs { get; set; } = 300;

    public bool AutoStart { get; set; } = false;

    /// <summary>Chat actions (built-in + user defined) with editable prompts.</summary>
    public List<ChatMode> Modes { get; set; } = new()
    {
        new() { Name = "回答问题", Builtin = true, Prompt = "你是一个乐于助人的 AI 助手。请用简体中文回答，回答尽量简洁、准确。" },
        new() { Name = "解释", Builtin = true, Prompt = "请解释用户提供的内容，用简体中文回答，条理清晰，通俗易懂。" },
        new() { Name = "翻译", Builtin = true, Prompt = "请将用户提供的内容翻译成简体中文；如果原文已经是中文，则翻译成英文。只输出译文，不要额外说明。" },
        new() { Name = "润色", Builtin = true, Prompt = "请润色用户提供的文字，使其更通顺、专业、简洁，保持原意，用简体中文输出。" },
        new() { Name = "自定义", Builtin = true, Prompt = "" }
    };

    /// <summary>Enable DeepSeek-style thinking mode (sends a "thinking" block).</summary>
    public bool ThinkingEnabled { get; set; } = false;

    /// <summary>Thinking budget in tokens; 0 = let the provider decide.</summary>
    public int ThinkingBudgetTokens { get; set; } = 0;

    /// <summary>Legacy "always search" toggle, kept for migration only.</summary>
    public bool SearchEnabled { get; set; } = false;

    /// <summary>Web search mode: Auto | Always | Off (null until first migration).</summary>
    public string? SearchMode { get; set; }

    /// <summary>Web search provider: Tavily | Google | Custom.</summary>
    public string SearchProvider { get; set; } = "Tavily";

    /// <summary>API key for Tavily.</summary>
    public string SearchApiKey { get; set; } = "";

    /// <summary>API key for Google search (Serper.dev).</summary>
    public string GoogleSearchApiKey { get; set; } = "";

    /// <summary>Custom search URL with {query} placeholder returning Tavily-style JSON.</summary>
    public string CustomSearchUrl { get; set; } = "";

    /// <summary>Access Secret for the Zhihu Open Platform (search + Zhida chat).</summary>
    public string ZhihuAccessSecret { get; set; } = "";

    /// <summary>Number of Zhihu search results to fetch before answering.</summary>
    public int ZhihuSearchCount { get; set; } = 5;
}
