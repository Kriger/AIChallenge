namespace AIChallenge.Models;

/// <summary>
/// Этап задачи в конечном автомате.
/// Основные этапы: Requirements → Planning → Execution → Validation → Done.
/// Служебные состояния (накладываются поверх основного этапа через флаг Paused):
///   Paused — задача приостановлена.
///   Resuming — задача возобновляется.
/// </summary>
public enum TaskStage
{
    /// <summary>
    /// Сбор требований — агент задаёт вопросы пользователю.
    /// </summary>
    Requirements,

    /// <summary>
    /// Планирование — на основе требований формируется план.
    /// </summary>
    Planning,

    /// <summary>
    /// Выполнение — реализация шагов плана.
    /// </summary>
    Execution,

    /// <summary>
    /// Валидация — проверка результатов.
    /// </summary>
    Validation,

    /// <summary>
    /// Задача завершена.
    /// </summary>
    Done,

    /// <summary>
    /// Служебное состояние: задача приостановлена (накладывается поверх основного этапа).
    /// </summary>
    Paused,

    /// <summary>
    /// Служебное состояние: задача возобновляется (накладывается поверх основного этапа).
    /// </summary>
    Resuming
}
