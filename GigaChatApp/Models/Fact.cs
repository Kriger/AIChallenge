namespace GigaChatApp.Models;

/// <summary>
/// Приоритет факта в долгосрочной памяти.
/// Влияет на порядок при eviction и бонус в скоринге поиска.
/// </summary>
public enum FactPriority
{
    /// <summary>Низкий приоритет — факты, извлечённые автоматически.</summary>
    Low = 0,

    /// <summary>Средний приоритет — факты с нормальной важностью.</summary>
    Normal = 1,

    /// <summary>Высокий приоритет — критически важные факты (профиль, решения).</summary>
    High = 2,

    /// <summary>Критический приоритет — факты, которые никогда не удаляются.</summary>
    Critical = 3,
}

/// <summary>
/// Факт — единица долгосрочной памяти.
/// </summary>
public class Fact
{
    /// <summary>Уникальный ключ факта.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Значение факта.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Откуда факт был получен (user, agent, explicit).</summary>
    public string Source { get; set; } = "user";

    /// <summary>Приоритет факта — влияет на eviction и скоринг.</summary>
    public FactPriority Priority { get; set; } = FactPriority.Normal;

    /// <summary>Время создания.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Время последнего чтения.</summary>
    public DateTime LastReadAt { get; set; }

    /// <summary>Количество раз, когда факт был использован.</summary>
    public int ReadCount { get; set; }
}
