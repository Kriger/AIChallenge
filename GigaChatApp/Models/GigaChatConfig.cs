namespace GigaChatApp.Models;

/// <summary>
/// Конфигурация GigaChat API.
/// Загружается из appsettings.json.
/// </summary>
public class GigaChatConfig
{
    /// <summary>
    /// Client ID из кабинета разработчика Sber.
    /// Получить: https://developer.sber.ru/portal/dev/products/gigachat
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client Secret из кабинета разработчика Sber.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Scope для OAuth2. Для API чата используется "GIGACHAT_API_PERS".
    /// </summary>
    public string Scope { get; set; } = "GIGACHAT_API_PERS";

    /// <summary>
    /// Модель GigaChat. Например: "GigaChat-3-Ultra".
    /// </summary>
    public string Model { get; set; } = "GigaChat-2";

    /// <summary>
    /// System-сообщение с инструкциями для модели. Если пусто — системное сообщение не отправляется.
    /// </summary>
    public string SystemMessage { get; set; } = string.Empty;

    /// <summary>
    /// Максимальное количество токенов в ответе. 0 означает без ограничения.
    /// </summary>
    public int MaxTokens { get; set; } = 0;

    /// <summary>
    /// Стоп-последовательности, завершающие генерацию ответа. Пустой массив — без ограничений.
    /// </summary>
    public string[] StopSequences { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Температура генерации. null — без ограничения. Допустимые значения: 0, 0.7, 1.2.
    /// </summary>
    public double? Temperature { get; set; } = null;

    // === Настройки управления контекстом ===

    /// <summary>
    /// Включено ли управление контекстом (сжатие истории через summary).
    /// false = полная история без сжатия (baseline для сравнения).
    /// </summary>
    public bool ContextCompressionEnabled { get; set; } = false;

    /// <summary>
    /// Количество последних сообщений, которые хранятся "как есть".
    /// </summary>
    public int ContextRecentMessageCount { get; set; } = 10;

    /// <summary>
    /// Интервал создания summary (каждые N сообщений).
    /// </summary>
    public int ContextSummaryInterval { get; set; } = 10;

    /// <summary>
    /// Максимальное количество summary, которые хранятся.
    /// </summary>
    public int ContextMaxSummaries { get; set; } = 20;

    /// <summary>
    /// Максимальное общее количество токенов для контекста.
    /// 0 = без ограничения.
    /// </summary>
    public int ContextMaxTokens { get; set; } = 0;

    /// <summary>
    /// Стратегия управления контекстом.
    /// SlidingWindow, StickyFacts, Branching.
    /// </summary>
    public string ContextStrategy { get; set; } = "SlidingWindow";

    /// <summary>
    /// Включено ли планирование (декомпозиция сложных запросов).
    /// false = все запросы обрабатываются напрямую, без разбивки на подзадачи.
    /// </summary>
    public bool PlannerEnabled { get; set; } = true;

    // === Настройки контекста ===

    /// <summary>
    /// Конфигурация управления контекстом.
    /// </summary>
    public ContextConfig Context { get; set; } = new();
}

/// <summary>
/// Конфигурация управления контекстом.
/// </summary>
public class ContextConfig
{
    /// <summary>
    /// Включено ли управление контекстом.
    /// false = полная история без сжатия.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Активная стратегия: SlidingWindow, StickyFacts, Branching.
    /// </summary>
    public string Strategy { get; set; } = "SlidingWindow";

    /// <summary>
    /// Настройки стратегии Sliding Window.
    /// </summary>
    public SlidingWindowConfig SlidingWindow { get; set; } = new();

    /// <summary>
    /// Настройки стратегии Sticky Facts.
    /// </summary>
    public StickyFactsConfig StickyFacts { get; set; } = new();

    /// <summary>
    /// Настройки стратегии Branching.
    /// </summary>
    public BranchingConfig Branching { get; set; } = new();

    /// <summary>
    /// Общие настройки summary (legacy-режим).
    /// </summary>
    public SummaryConfig Summary { get; set; } = new();

    /// <summary>
    /// Загружает конфигурацию из IConfiguration.
    /// </summary>
    public static ContextConfig Load(IConfigurationSection section)
    {
        var result = new ContextConfig();

        section.Bind(result);

        // Вложенные объекты — загружаем отдельно, так как Bind() не инициализирует их
        result.SlidingWindow = section.GetSection("SlidingWindow").Get<SlidingWindowConfig>() ?? new SlidingWindowConfig();
        result.StickyFacts = section.GetSection("StickyFacts").Get<StickyFactsConfig>() ?? new StickyFactsConfig();
        result.Branching = section.GetSection("Branching").Get<BranchingConfig>() ?? new BranchingConfig();
        result.Summary = section.GetSection("Summary").Get<SummaryConfig>() ?? new SummaryConfig();

        return result;
    }
}

/// <summary>
/// Настройки Sliding Window стратегии.
/// </summary>
public class SlidingWindowConfig
{
    /// <summary>
    /// Размер окна — количество последних сообщений, которые хранятся "как есть".
    /// </summary>
    public int WindowSize { get; set; } = 10;
}

/// <summary>
/// Настройки Sticky Facts стратегии.
/// </summary>
public class StickyFactsConfig
{
    /// <summary>
    /// Размер окна — количество последних сообщений, которые хранятся "как есть".
    /// </summary>
    public int WindowSize { get; set; } = 10;

    /// <summary>
    /// Максимальное количество фактов, которые хранятся.
    /// </summary>
    public int MaxFacts { get; set; } = 50;
}

/// <summary>
/// Настройки Branching стратегии.
/// </summary>
public class BranchingConfig
{
    /// <summary>
    /// Максимальное количество веток. 0 = без ограничения.
    /// </summary>
    public int MaxBranches { get; set; } = 0;

    /// <summary>
    /// Максимальное количество чекпоинтов. 0 = без ограничения.
    /// </summary>
    public int MaxCheckpoints { get; set; } = 0;
}

/// <summary>
/// Общие настройки summary (legacy-режим сжатия).
/// </summary>
public class SummaryConfig
{
    /// <summary>
    /// Интервал создания summary (каждые N сообщений).
    /// </summary>
    public int Interval { get; set; } = 10;

    /// <summary>
    /// Максимальное количество summary, которые хранятся.
    /// </summary>
    public int MaxSummaries { get; set; } = 20;

    /// <summary>
    /// Максимальное общее количество токенов для контекста.
    /// 0 = без ограничения.
    /// </summary>
    public int MaxContextTokens { get; set; } = 0;
}
