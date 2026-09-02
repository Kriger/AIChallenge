namespace GigaChatApp;

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
}
