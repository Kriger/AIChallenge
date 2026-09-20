namespace GigaChatApp.Models;

/// <summary>
/// Информация о текущем шаге внутри этапа.
/// </summary>
public readonly record struct TaskStep(
    int Number,
    int Total,
    string Description);
