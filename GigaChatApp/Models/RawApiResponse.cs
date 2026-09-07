namespace GigaChatApp.Models;

/// <summary>
/// Сырой ответ от API GigaChat.
/// </summary>
public class RawApiResponse
{
    public string Content { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public TokenUsage? Usage { get; set; }
}
