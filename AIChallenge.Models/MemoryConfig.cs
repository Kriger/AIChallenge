namespace AIChallenge.Models;

/// <summary>
/// Конфигурация краткосрочной памяти.
/// </summary>
public class ShortTermMemoryConfig
{
    /// <summary>Максимальное количество записей.</summary>
    public int MaxSize { get; set; } = 100;

    /// <summary>Включить decay (затухание старых записей).</summary>
    public bool DecayEnabled { get; set; } = false;

    /// <summary>Время в часах после которого запись помечается как затухшая.</summary>
    public int DecayAfterHours { get; set; } = 1;
}

/// <summary>
/// Конфигурация рабочей памяти.
/// </summary>
public class WorkingMemoryConfig
{
    /// <summary>Включить автоматическую архивацию завершённых задач.</summary>
    public bool ArchiveEnabled { get; set; } = true;

    /// <summary>Максимальное количество архивных записей на задачу.</summary>
    public int MaxArchiveSize { get; set; } = 10;
}

/// <summary>
/// Конфигурация долгосрочной памяти.
/// </summary>
public class LongTermMemoryConfig
{
    /// <summary>Максимальное количество фактов.</summary>
    public int MaxSize { get; set; } = 500;
}

/// <summary>
/// Конфигурация памяти агента.
/// </summary>
public class MemoryConfig
{
    /// <summary>Настройки краткосрочной памяти.</summary>
    public ShortTermMemoryConfig ShortTerm { get; set; } = new();

    /// <summary>Настройки рабочей памяти.</summary>
    public WorkingMemoryConfig Working { get; set; } = new();

    /// <summary>Настройки долгосрочной памяти.</summary>
    public LongTermMemoryConfig LongTerm { get; set; } = new();
}
