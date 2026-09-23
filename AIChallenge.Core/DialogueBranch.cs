using AIChallenge.Models;

namespace AIChallenge.Core.Infrastructure;

/// <summary>
/// Представление ветки диалога.
/// </summary>
public class DialogueBranch
{
    /// <summary>Уникальный идентификатор ветки.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Читаемое название ветки.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Сообщения в этой ветке.</summary>
    public List<ApiMessage> Messages { get; set; } = new();

    /// <summary>Время создания ветки.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Время последнего изменения.</summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    /// <summary>True, если это основная (master) ветка.</summary>
    public bool IsMain { get; set; }

    /// <summary>Факты, привязанные к этой ветке (ключ-значение).</summary>
    public Dictionary<string, string> Facts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Создать глубокую копию ветки.
    /// </summary>
    public DialogueBranch Clone(string newId, string newName)
    {
        return new DialogueBranch
        {
            Id = newId,
            Name = newName,
            Messages = Messages.Select(m => new ApiMessage { Role = m.Role, Content = m.Content }).ToList(),
            Facts = new Dictionary<string, string>(Facts, StringComparer.OrdinalIgnoreCase),
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.UtcNow,
            IsMain = false,
        };
    }
}
