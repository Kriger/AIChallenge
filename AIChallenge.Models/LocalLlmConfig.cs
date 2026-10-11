namespace AIChallenge.Models;

/// <summary>
/// Профили оптимизации локальной LLM.
/// </summary>
public enum LlmOptimizationProfile
{
    /// <summary>
    /// Базовый профиль (по умолчанию).
    /// </summary>
    Default,

    /// <summary>
    /// Детерминированный — для сбора требований, валидации, планирования.
    /// Низкая температура, строгий вывод.
    /// </summary>
    Deterministic,

    /// <summary>
    /// Сбалансированный — для архитектурных дискуссий, trade-offs.
    /// Умеренная температура, хороший баланс.
    /// </summary>
    Balanced,

    /// <summary>
    /// Максимальная скорость — для быстрых ответов, коротких вопросов.
    /// Высокая температура, ограниченный контекст.
    /// </summary>
    Speed
}

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
    /// Профиль оптимизации. Автоматически настраивает temperature, max_tokens и другие параметры.
    /// </summary>
    public LlmOptimizationProfile OptimizationProfile { get; set; } = LlmOptimizationProfile.Default;

    /// <summary>
    /// Температура генерации (0.0–2.0). 0.0 = детерминированно, 1.0 = креативно.
    /// Переопределяется профилем оптимизации.
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Максимальное количество токенов в ответе. 0 = без ограничения.
    /// </summary>
    public int MaxTokens { get; set; } = 0;

    /// <summary>
    /// Top-K sampling — количество самых вероятных токенов для рассмотрения.
    /// Ниже = более детерминированно. 15-20 для архитектурных задач.
    /// 0 = использовать значение по умолчанию модели.
    /// </summary>
    public int TopK { get; set; } = 0;

    /// <summary>
    /// Top-P sampling — накопленная вероятность для рассмотрения токенов.
    /// 0.9-0.95 для баланса между креативностью и точностью.
    /// 0 = использовать значение по умолчанию модели.
    /// </summary>
    public double TopP { get; set; } = 0.0;

    /// <summary>
    /// Штраф за повторение токенов. >1.0 снижает повторы.
    /// 1.0-1.2 для архитектурных задач.
    /// 0 = использовать значение по умолчанию модели.
    /// </summary>
    public double RepeatPenalty { get; set; } = 0.0;

    /// <summary>
    /// Размер контекстного окна в токенах (num_ctx).
    /// 4096-8192 для большинства задач. 16384+ для длинных документов.
    /// 0 = использовать значение по умолчанию модели.
    /// </summary>
    public int NumCtx { get; set; } = 0;

    /// <summary>
    /// Размер батча (num_batch). Влияет на параллелизм при обработке контекста.
    /// 1024-4096. Должно быть кратно размеру контекста.
    /// 0 = использовать значение по умолчанию модели.
    /// </summary>
    public int NumBatch { get; set; } = 0;

    /// <summary>
    /// Количество потоков CPU для инференса.
    /// Для Ryzen 5 5600H (6 ядер/12 потоков): 6 для нагрузки без UI, 12 для фонового режима.
    /// 0 = автоопределение (все ядра).
    /// </summary>
    public int NumThread { get; set; } = 0;

    /// <summary>
    /// Таймаут запроса в секундах. По умолчанию: 120.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Применяет значения по умолчанию из профиля оптимизации.
    /// Вызывается при загрузке конфигурации.
    /// </summary>
    public void ApplyProfileDefaults()
    {
        switch (OptimizationProfile)
        {
            case LlmOptimizationProfile.Deterministic:
                Temperature = Math.Min(Temperature, 0.1) == 0 ? 0.1 : Temperature;
                if (MaxTokens == 0) MaxTokens = 4096;
                if (TopK == 0) TopK = 15;
                if (TopP == 0) TopP = 0.9;
                if (RepeatPenalty == 0) RepeatPenalty = 1.1;
                if (NumCtx == 0) NumCtx = 8192;
                if (NumBatch == 0) NumBatch = 2048;
                if (NumThread == 0) NumThread = 6;
                break;

            case LlmOptimizationProfile.Balanced:
                Temperature = Math.Min(Temperature, 0.3) == 0 ? 0.3 : Temperature;
                if (MaxTokens == 0) MaxTokens = 4096;
                if (TopK == 0) TopK = 20;
                if (TopP == 0) TopP = 0.95;
                if (RepeatPenalty == 0) RepeatPenalty = 1.05;
                if (NumCtx == 0) NumCtx = 8192;
                if (NumBatch == 0) NumBatch = 2048;
                if (NumThread == 0) NumThread = 6;
                break;

            case LlmOptimizationProfile.Speed:
                if (MaxTokens == 0) MaxTokens = 1024;
                if (TopK == 0) TopK = 50;
                if (TopP == 0) TopP = 0.99;
                if (RepeatPenalty == 0) RepeatPenalty = 1.0;
                if (NumCtx == 0) NumCtx = 4096;
                if (NumBatch == 0) NumBatch = 1024;
                if (NumThread == 0) NumThread = 12;
                break;

            case LlmOptimizationProfile.Default:
            default:
                // Оставляем значения из appsettings.json без изменений
                break;
        }
    }
}
