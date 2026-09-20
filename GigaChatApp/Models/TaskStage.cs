namespace GigaChatApp.Models;

/// <summary>
/// Этап задачи в конечном автомате.
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
    Done
}
