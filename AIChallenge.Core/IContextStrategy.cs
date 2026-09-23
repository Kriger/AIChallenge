using AIChallenge.Models;

namespace AIChallenge.Core.Infrastructure;

/// <summary>
/// Интерфейс стратегии управления контекстом.
/// </summary>
public interface IContextStrategy
{
    /// <summary>Название стратегии.</summary>
    string Name { get; }

    /// <summary>Описание стратегии.</summary>
    string Description { get; }

    /// <summary>Добавить сообщение в контекст.</summary>
    void AddMessage(ApiMessage message);

    /// <summary>
    /// Построить контекст для отправки в API.
    /// </summary>
    ContextResult BuildContext();

    /// <summary>Очистить весь контекст.</summary>
    void Clear();

    /// <summary>Загрузить историю из внешнего источника.</summary>
    void LoadHistory(IEnumerable<ApiMessage> messages);

    /// <summary>Получить информацию о текущем состоянии контекста.</summary>
    string GetStatus();
}
