namespace AskAnywhere.Models;

/// <summary>A saved chat session.</summary>
public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<AskAnywhere.Services.ChatMessage> Messages { get; set; } = new();
}
