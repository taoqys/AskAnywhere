namespace AskAnywhere.Models;

/// <summary>A chat action with a customizable system prompt.</summary>
public sealed class ChatMode
{
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    public bool Builtin { get; set; } = false;
}
