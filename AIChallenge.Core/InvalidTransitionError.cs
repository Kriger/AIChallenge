using AIChallenge.Models;

namespace AIChallenge.Core.Infrastructure;

/// <summary>
/// Исключение, выбрасываемое при попытке недопустимого перехода между состояниями FSM.
/// Содержит текущее состояние, целевое состояние и список разрешённых переходов.
/// </summary>
public sealed class InvalidTransitionError : Exception
{
    /// <summary>
    /// Текущее состояние автомата.
    /// </summary>
    public TaskStage CurrentStage { get; }

    /// <summary>
    /// Целевое состояние, в которое пытались перейти.
    /// </summary>
    public TaskStage TargetStage { get; }

    /// <summary>
    /// Список состояний, в которые можно перейти из текущего.
    /// </summary>
    public IReadOnlyList<TaskStage> AllowedNext { get; }

    /// <summary>
    /// Подробное сообщение о причине запрета перехода.
    /// </summary>
    public string Reason { get; }

    public InvalidTransitionError(TaskStage currentStage, TaskStage targetStage, IReadOnlyList<TaskStage> allowedNext, string reason)
        : base(reason)
    {
        CurrentStage = currentStage;
        TargetStage = targetStage;
        AllowedNext = allowedNext ?? Array.Empty<TaskStage>();
        Reason = reason;
    }

    /// <summary>
    /// Формирует человеко-читаемое сообщение об ошибке.
    /// </summary>
    public override string Message =>
        $"Недопустимый переход: '{CurrentStage}' → '{TargetStage}'. {Reason} " +
        $"Разрешённые переходы: [{string.Join(", ", AllowedNext)}]";

    /// <summary>
    /// Сериализует ошибку в JSON-формате.
    /// </summary>
    public string ToJson()
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            allowed = false,
            reason = Reason,
            current_stage = CurrentStage.ToString().ToLowerInvariant(),
            target_stage = TargetStage.ToString().ToLowerInvariant(),
            allowed_next = AllowedNext.Select(s => s.ToString().ToLowerInvariant()).ToArray()
        }, options);
    }
}
