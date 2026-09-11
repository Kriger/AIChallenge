namespace GigaChatApp.Services;

/// <summary>
/// Оценка количества токенов в тексте.
/// Использует эвристику: ~3.5 символа на токен для русского/английского.
/// </summary>
public static class TokenEstimator
{
    /// <summary>Среднее количество символов на токен (русский/английский).</summary>
    private const double CharsPerToken = 3.5;

    /// <summary>Токены на одно сообщение в структуре API (role, content, metadata).</summary>
    private const int TokensPerMessage = 4;

    /// <summary>Токены на системное сообщение (структура).</summary>
    private const int SystemMessageOverhead = 3;

    /// <summary>Токены на модель и параметры запроса.</summary>
    private const int RequestOverhead = 5;

    /// <summary>
    /// Оценивает количество токенов в тексте.
    /// </summary>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / CharsPerToken);
    }

    /// <summary>
    /// Оценивает количество токенов для текущего запроса пользователя.
    /// </summary>
    public static int EstimateRequestTokens(string userMessage)
    {
        return EstimateTokens(userMessage);
    }

    /// <summary>
    /// Оценивает количество токенов для всей истории диалога.
    /// Включает все сообщения + системное сообщение.
    /// </summary>
    public static int EstimateHistoryTokens(
        IEnumerable<Models.ApiMessage> history,
        string systemMessage = "")
    {
        var totalTokens = 0;

        // Системное сообщение
        if (!string.IsNullOrEmpty(systemMessage))
        {
            totalTokens += SystemMessageOverhead;
            totalTokens += EstimateTokens(systemMessage);
        }

        // История сообщений
        foreach (var msg in history)
        {
            totalTokens += TokensPerMessage;
            totalTokens += EstimateTokens(msg.Role);
            totalTokens += EstimateTokens(msg.Content);
        }

        return totalTokens;
    }

    /// <summary>
    /// Оценивает полное количество токенов для отправки в API.
    /// Включает историю, системное сообщение и запрос.
    /// </summary>
    public static int EstimateTotalPromptTokens(
        List<Models.ApiMessage> history,
        string systemMessage,
        string userMessage)
    {
        return RequestOverhead + EstimateHistoryTokens(history, systemMessage) + EstimateRequestTokens(userMessage);
    }
}
