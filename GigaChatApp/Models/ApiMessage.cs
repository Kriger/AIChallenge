namespace GigaChatApp.Models;

/// <summary>
/// Сообщение для отправки в API.
/// </summary>
public class ApiMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
