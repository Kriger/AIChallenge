namespace GigaChatApp.Models;

/// <summary>
/// План выполнения сложного запроса.
/// Содержит список подзадач и статус выполнения.
/// </summary>
public class Plan
{
    /// <summary>Исходный запрос пользователя.</summary>
    public string OriginalRequest { get; set; } = string.Empty;

    /// <summary>Список подзадач.</summary>
    public List<TaskItem> Tasks { get; } = new();

    /// <summary>Финальный комбинированный ответ.</summary>
    public string? FinalAnswer { get; set; }

    /// <summary>Общая длительность выполнения плана.</summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>
    /// Получить статус выполнения плана.
    /// </summary>
    public PlanStatus Status => Tasks.Count == 0 ? PlanStatus.Empty
        : Tasks.All(t => t.Status == TaskStatus.Completed) ? PlanStatus.Completed
        : Tasks.Any(t => t.Status == TaskStatus.Running) ? PlanStatus.Running
        : Tasks.Any(t => t.Status == TaskStatus.Failed) ? PlanStatus.PartiallyFailed
        : PlanStatus.Pending;

    /// <summary>
    /// Получить статус в виде строки.
    /// </summary>
    public string StatusText => Status switch
    {
        PlanStatus.Empty => "📋 План пуст",
        PlanStatus.Pending => "⏳ Ожидание выполнения",
        PlanStatus.Running => "🔄 Выполняется",
        PlanStatus.Completed => "✅ Выполнен",
        PlanStatus.PartiallyFailed => "⚠️ Частично выполнен",
        _ => "???",
    };
}
