namespace GigaChatApp.Infrastructure;

/// <summary>
/// Внутренний класс для хранения записи в кэше.
/// </summary>
internal class CachedEntry
{
    public string Answer { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
