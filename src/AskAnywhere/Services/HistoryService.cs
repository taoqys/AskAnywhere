using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AskAnywhere.Models;

namespace AskAnywhere.Services;

/// <summary>
/// Persists finished chat sessions to %APPDATA%\AskAnywhere\history.json.
/// Each time the chat window is hidden, the current conversation is saved here
/// and the in-memory conversation is reset, so re-opening always starts fresh.
/// </summary>
public static class HistoryService
{
    private static readonly Lazy<string> FilePathLazy = new(() =>
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AskAnywhere");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "history.json");
    });

    private static readonly object Lock = new();

    public static string FilePath => FilePathLazy.Value;

    public static List<ChatSession> LoadAll()
    {
        lock (Lock)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var list = JsonSerializer.Deserialize<List<ChatSession>>(json);
                    if (list != null)
                    {
                        return list;
                    }
                }
            }
            catch
            {
                // Ignore corrupted history.
            }
            return new List<ChatSession>();
        }
    }

    public static void Add(ChatSession session)
    {
        if (session == null || session.Messages == null || session.Messages.Count == 0)
        {
            return;
        }

        lock (Lock)
        {
            try
            {
                var list = LoadAll();
                list.Add(session);
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // Ignore history write failures.
            }
        }
    }

    public static void ClearAll()
    {
        lock (Lock)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch
            {
                // Ignore.
            }
        }
    }
}
