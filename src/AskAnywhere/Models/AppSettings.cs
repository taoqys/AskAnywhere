namespace AskAnywhere.Models;

public sealed class AppSettings
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.7;
    public bool AutoSendOnSelection { get; set; } = false;
    public bool AutoHideOnDeactivate { get; set; } = true;
    public int DoubleCtrlThresholdMs { get; set; } = 300;
    public bool AutoStart { get; set; } = false;
    public string CustomPrompt { get; set; } = "";
}
