namespace AIChallenge.Models;

/// <summary>
/// Конфигурация локальной LLM (Ollama).
/// </summary>
public class LocalLlmConfig
{
    /// <summary>
    /// Включена ли локальная модель.
    /// true — запросы идут в Ollama, false — в GigaChat API.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// URL Ollama API. По умолчанию: http://localhost:11434
    /// </summary>
    public string Url { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Название модели Ollama. Например: "llama3.1", "mistral", "qwen2.5".
    /// Модель должна быть предварительно загружена: `ollama pull &lt;model&gt;`.
    /// </summary>
    public string Model { get; set; } = "llama3.1";

    /// <summary>
    /// Температура генерации (0.0–2.0). 0.0 = детерминированно, 1.0 = креативно.
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Максимальное количество токенов в ответе. 0 = без ограничения.
    /// </summary>
    public int MaxTokens { get; set; } = 0;

    /// <summary>
    /// Таймаут запроса в секундах. По умолчанию: 120.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}
