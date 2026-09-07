namespace GigaChatApp.Models;

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

    /// <summary>Время создания.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Время последнего чтения.</summary>
    public DateTime LastReadAt { get; set; }

    /// <summary>Количество раз, когда факт был использован.</summary>
    public int ReadCount { get; set; }
}
