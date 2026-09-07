namespace GigaChatApp.Models;

/// <summary>
/// Классификация ошибки для принятия решения о retry.
/// </summary>
public enum ErrorCategory
{
    /// <summary>Сетевая ошибка / таймаут — стоит повторить.</summary>
    Transient,

    /// <summary>Ошибки сервера (5xx) — стоит повторить.</summary>
    ServerError,

    /// <summary>Клиентская ошибка (4xx) — повтор не поможет.</summary>
    ClientError,

    /// <summary>Неизвестная ошибка — не повторять.</summary>
    Unknown
}
