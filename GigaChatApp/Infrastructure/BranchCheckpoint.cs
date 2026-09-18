using GigaChatApp.Models;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Checkpoint — момент для создания ветки.
/// </summary>
public class BranchCheckpoint
{
    /// <summary>Идентификатор чекпоинта.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Читаемое название.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Номер сообщения, с которого начинается ветка (индекс в messages).</summary>
    public int MessageIndex { get; set; }

    /// <summary>На какой ветке был создан чекпоинт.</summary>
    public string BranchId { get; set; } = string.Empty;

    /// <summary>Факты на момент создания чекпоинта.</summary>
    public Dictionary<string, string> Facts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Время создания.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Сколько сообщений было в ветке на момент создания.</summary>
    public int MessageCount { get; set; }
}
