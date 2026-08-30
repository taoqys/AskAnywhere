using System.Collections.Generic;

namespace AskAnywhere.Models;

/// <summary>A configured OpenAI-compatible chat provider (Base URL + Key + model).</summary>
public sealed class ChatProvider
{
    public string Name { get; set; } = "默认";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";

    /// <summary>Protocol kind: "OpenAI" (compatible chat/completions) or "Zhihu" (Zhida).</summary>
    public string Kind { get; set; } = "OpenAI";

    /// <summary>Cached model list fetched from this provider (empty until refreshed).</summary>
    public List<string> Models { get; set; } = new();
}
