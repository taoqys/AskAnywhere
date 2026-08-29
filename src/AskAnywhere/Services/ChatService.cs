using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AskAnywhere.Services;

public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    public ChatMessage()
    {
    }

    public ChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}

/// <summary>A streaming chunk: the final answer (Content) and/or the hidden
/// chain-of-thought (Reasoning, DeepSeek "reasoning_content").</summary>
public sealed class ChatDelta
{
    public string? Content { get; init; }
    public string? Reasoning { get; init; }

    public ChatDelta(string? content, string? reasoning)
    {
        Content = content;
        Reasoning = reasoning;
    }
}

public sealed class ChatException : Exception
{
    public ChatException(string message) : base(message)
    {
    }
}

/// <summary>
/// Minimal streaming client for any OpenAI-compatible chat/completions API.
/// </summary>
public sealed class ChatService
{
    private const string ZhihuChatUrl = "https://developer.zhihu.com/v1/chat/completions";

    /// <summary>Models exposed by the Zhihu provider (知乎直答).</summary>
    public static readonly string[] ZhihuModels =
    {
        "zhida-fast-1p5",
        "zhida-thinking-1p5",
        "zhida-agent"
    };

    private readonly HttpClient _http;

    public ChatService()
    {
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async IAsyncEnumerable<ChatDelta> StreamChatAsync(
        string baseUrl,
        string apiKey,
        string model,
        double temperature,
        bool thinkingEnabled,
        int thinkingBudgetTokens,
        string? reasoningEffort,
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = BuildUrl(baseUrl);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);

        if (!string.IsNullOrEmpty(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true,
            ["temperature"] = temperature
        };

        // DeepSeek-style APIs reason by default, so an explicit
        // "disabled" must be sent to actually turn reasoning off.
        // Other OpenAI-compatible providers usually ignore this block.
        var thinking = new Dictionary<string, object?> { ["type"] = thinkingEnabled ? "enabled" : "disabled" };
        if (thinkingEnabled && thinkingBudgetTokens > 0)
        {
            thinking["budget_tokens"] = thinkingBudgetTokens;
        }
        payload["thinking"] = thinking;

        // DeepSeek V4 style effort control.
        if (thinkingEnabled && !string.IsNullOrEmpty(reasoningEffort))
        {
            payload["reasoning_effort"] = reasoningEffort;
        }

        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(ct);
            throw new ChatException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {errorBody}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await foreach (var delta in ReadSseAsync(stream, ct))
        {
            yield return delta;
        }
    }

    /// <summary>
    /// Streams a Zhida (知乎直答) completion. The endpoint is OpenAI-compatible
    /// and uses the same SSE shape (delta.content / delta.reasoning_content),
    /// but it authenticates with the Zhihu Access Secret plus a timestamp.
    /// </summary>
    public async IAsyncEnumerable<ChatDelta> StreamZhihuAsync(
        string accessSecret,
        string model,
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ZhihuChatUrl);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessSecret);
        req.Headers.TryAddWithoutValidation("X-Request-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true
        };
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(ct);
            throw new ChatException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {errorBody}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await foreach (var delta in ReadSseAsync(stream, ct))
        {
            yield return delta;
        }
    }

    /// <summary>
    /// Shard SSE reader: it parses OpenAI-compatible chunks into ChatDelta
    /// (final answer + optional hidden reasoning), skipping heartbeats and
    /// finishing on [DONE].
    /// </summary>
    private static async IAsyncEnumerable<ChatDelta> ReadSseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line.Substring(5).Trim();
            if (data == "[DONE]")
            {
                break;
            }

            string? content = null;
            string? reasoning = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("delta", out var deltaEl))
                    {
                        if (deltaEl.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                        {
                            content = contentEl.GetString();
                        }
                        if (deltaEl.TryGetProperty("reasoning_content", out var reasoningEl) && reasoningEl.ValueKind == JsonValueKind.String)
                        {
                            reasoning = reasoningEl.GetString();
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Skip malformed SSE lines.
            }

            if (!string.IsNullOrEmpty(content) || !string.IsNullOrEmpty(reasoning))
            {
                yield return new ChatDelta(content, reasoning);
            }
        }
    }

    /// <summary>Fetches the model list from an OpenAI-compatible GET /models endpoint.</summary>
    public async Task<List<string>> GetModelsAsync(string baseUrl, string apiKey, CancellationToken ct)
    {
        var url = (baseUrl ?? "").Trim().TrimEnd('/') + "/models";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(ct);
            throw new ChatException($"HTTP {(int)resp.StatusCode}: {errorBody}");
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var list = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    list.Add(id.GetString()!);
                }
            }
        }
        return list;
    }

    private static string BuildUrl(string baseUrl)
    {
        var url = baseUrl?.Trim() ?? "";
        if (string.IsNullOrEmpty(url))
        {
            return "https://api.openai.com/v1/chat/completions";
        }
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }
        return url.TrimEnd('/') + "/chat/completions";
    }
}
