namespace GigaChatApp.Models;

/// <summary>
/// Статус выполнения плана.
/// </summary>
public enum PlanStatus
{
    Empty,
    Pending,
    Running,
    Completed,
    PartiallyFailed,
}
