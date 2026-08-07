using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AskAnywhere.Services;

/// <summary>A single web search hit.</summary>
public sealed class SearchResult
{
    public string Title { get; set; } = "";
    public string Snippet { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>
/// Web search used before answering when the "联网" toggle is on.
/// Primary provider: Tavily (JSON API, key configured by the user). A custom
/// URL returning Tavily-style JSON is also supported for flexibility.
/// </summary>
public sealed class WebSearchService
{
    private const string TavilyEndpoint = "https://api.tavily.com/search";
    private const int MaxResults = 6;
    private const int SnippetMaxLength = 500;

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AskAnywhere/0.2");
        return client;
    }

    public async Task<List<SearchResult>> SearchAsync(
        string query,
        string provider,
        string apiKey,
        string customUrl,
        CancellationToken ct)
    {
        var normalized = (provider ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "custom" => await SearchCustomAsync(query, customUrl, ct),
            _ => await SearchTavilyAsync(query, apiKey, ct)
        };
    }

    private async Task<List<SearchResult>> SearchTavilyAsync(string query, string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new List<SearchResult>();
        }

        var payload = new Dictionary<string, object?>
        {
            ["api_key"] = apiKey,
            ["query"] = query,
            ["max_results"] = MaxResults,
            ["search_depth"] = "basic"
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, TavilyEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json")
        };
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Tavily HTTP {(int)resp.StatusCode}: {Truncate(err, 200)}");
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        return ParseResultsJson(json);
    }

    private async Task<List<SearchResult>> SearchCustomAsync(string query, string customUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(customUrl))
        {
            return new List<SearchResult>();
        }

        var url = customUrl.Replace("{query}", Uri.EscapeDataString(query));
        var json = await Http.GetStringAsync(url, ct);
        return ParseResultsJson(json);
    }

    /// <summary>Parses a Tavily-style response: {"results":[{"title","url","content"}]}.</summary>
    private static List<SearchResult> ParseResultsJson(string json)
    {
        var results = new List<SearchResult>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            if (doc.RootElement.TryGetProperty("results", out var resultsProp))
            {
                arr = resultsProp;
            }

            if (arr.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var item in arr.EnumerateArray())
            {
                var title = GetString(item, "title");
                var snippet = GetString(item, "content") ?? GetString(item, "snippet") ?? "";
                var url = GetString(item, "url");

                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                results.Add(new SearchResult
                {
                    Title = Clean(title),
                    Snippet = Truncate(Clean(snippet), SnippetMaxLength),
                    Url = Clean(url)
                });

                if (results.Count >= MaxResults)
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed response: return what we have (usually nothing).
        }
        return results;
    }

    private static string? GetString(JsonElement el, string name)
    {
        return el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }

    private static string Clean(string? text)
        => (text ?? "").Trim();

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text.Substring(0, maxLength) + "…";
}
