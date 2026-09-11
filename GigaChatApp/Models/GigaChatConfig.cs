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
}
