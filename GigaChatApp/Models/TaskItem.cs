namespace GigaChatApp.Models;

/// <summary>
/// Подзадача в плане выполнения.
/// </summary>
public class TaskItem
{
    /// <summary>Уникальный идентификатор подзадачи.</summary>
    public int Id { get; set; }

    /// <summary>Описание подзадачи.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Контекст для выполнения (релевантные факты, предыдущие результаты).</summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>Результат выполнения подзадачи.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Статус выполнения.</summary>
    public TaskStatus Status { get; set; } = TaskStatus.Pending;

    /// <summary>Время начала выполнения.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Время завершения выполнения.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Длительность выполнения.</summary>
    public TimeSpan? Duration => CompletedAt - StartedAt;

    /// <summary>Сообщение об ошибке, если подзадача не выполнена.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// Получить строковое представление статуса.
    /// </summary>
    public string StatusText => Status switch
    {
        TaskStatus.Pending => "⏳ Ожидание",
        TaskStatus.Running => "🔄 Выполняется",
        TaskStatus.Completed => "✅ Выполнено",
        TaskStatus.Failed => "❌ Ошибка",
        _ => "???",
    };
}
