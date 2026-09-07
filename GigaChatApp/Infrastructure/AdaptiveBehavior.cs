namespace GigaChatApp.Infrastructure;

/// <summary>
/// Адаптивное поведение агента.
/// Автоматически подстраивает параметры на основе метрик.
/// </summary>
public class AdaptiveBehavior
{
    private readonly AgentMetrics _metrics;
    private readonly AgentLogger _logger;
    private readonly RequestCache _cache;

    /// <summary>
    /// Порог success rate для активации адаптации.
    /// Если success_rate < threshold → включаем агрессивную адаптацию.
    /// </summary>
    public double FailureThreshold { get; set; } = 0.3;

    /// <summary>
    /// Минимальное количество запросов для анализа.
    /// Адаптация срабатывает только после этого количества запросов.
    /// </summary>
    public int MinSamples { get; set; } = 5;

    /// <summary>
    /// Коэффициент увеличения retry delay при ошибках.
    /// </summary>
    public double RetryDelayMultiplier { get; set; } = 1.5;

    /// <summary>
    /// Максимальный retry delay (мс).
    /// </summary>
    public int MaxRetryDelayMs { get; set; } = 10000;

    /// <summary>
    /// Коэффициент увеличения max retry count при ошибках.
    /// </summary>
    public int RetryCountIncrement { get; set; } = 1;

    /// <summary>
    /// Максимальный retry count.
    /// </summary>
    public int MaxRetryCount { get; set; } = 5;

    /// <summary>
    /// Порог hit rate кэша для увеличения его размера.
    /// </summary>
    public double CacheHitRateThreshold { get; set; } = 0.5;

    /// <summary>
    /// Коэффициент увеличения размера кэша при высоком hit rate.
    /// </summary>
    public double CacheSizeMultiplier { get; set; } = 1.5;

    /// <summary>
    /// Максимальный размер кэша.
    /// </summary>
    public int MaxCacheSize { get; set; } = 1000;

    /// <summary>
    /// Флаг включения адаптации.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Последнее применённое изменение.
    /// </summary>
    public string? LastChange { get; private set; }

    /// <summary>
    /// Время последнего применения адаптации.
    /// </summary>
    public DateTime? LastAdaptationTime { get; private set; }

    public AdaptiveBehavior(AgentMetrics metrics, RequestCache cache, AgentLogger? logger = null)
    {
        _metrics = metrics;
        _cache = cache;
        _logger = logger ?? new AgentLogger(LogLevel.Info);
    }

    /// <summary>
    /// Проверяет метрики и применяет адаптацию при необходимости.
    /// Вызывать после каждого запроса.
    /// </summary>
    public void Adapt()
    {
        if (!Enabled)
            return;

        if (_metrics.TotalRequests < MinSamples)
            return;

        var successRate = _metrics.SuccessRate;
        var cacheHitRate = CalculateCacheHitRate();

        // 1. Высокий процент ошибок → увеличиваем retry delay и count
        if (successRate < FailureThreshold)
        {
            AdaptForFailures();
            return;
        }

        // 2. Высокий hit rate кэша → увеличиваем размер кэша
        if (cacheHitRate > CacheHitRateThreshold && _metrics.CachedRequests > 10)
        {
            AdaptForCache();
            return;
        }

        // 3. Низкий процент ошибок → постепенно снижаем retry delay (оптимизация)
        if (successRate > 0.9 && _metrics.RetryDelayMs > 1000)
        {
            AdaptForOptimization();
            return;
        }
    }

    /// <summary>
    /// Сбрасывает адаптацию к значениям по умолчанию.
    /// </summary>
    public void Reset()
    {
        _metrics.RetryDelayMs = 1000;
        _metrics.RetryCount = 2;
        LastChange = "Сброшено к значениям по умолчанию";
        LastAdaptationTime = DateTime.UtcNow;
        _logger.Info("Адаптация: параметры сброшены к значениям по умолчанию");
    }

    /// <summary>
    /// Получает текущий статус адаптации.
    /// </summary>
    public string GetStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine("🔄 Адаптация:");
        sb.AppendLine($"   Включена: {Enabled}");
        sb.AppendLine($"   Порог ошибок: {FailureThreshold:P0}");
        sb.AppendLine($"   MinSamples: {MinSamples}");
        sb.AppendLine($"   Retry delay: {_metrics.RetryDelayMs} мс");
        sb.AppendLine($"   Retry count: {_metrics.RetryCount}");
        sb.AppendLine($"   Размер кэша: {_cache.Count}");
        sb.AppendLine($"   Cache hit rate: {CalculateCacheHitRate():P0}");
        sb.AppendLine($"   Success rate: {_metrics.SuccessRate:P0}");

        if (LastChange is not null)
        {
            sb.AppendLine($"   Последнее изменение: {LastChange}");
            sb.AppendLine($"   Время: {LastAdaptationTime:HH:mm:ss}");
        }

        return sb.ToString();
    }

    private double CalculateCacheHitRate()
    {
        if (_metrics.TotalRequests == 0)
            return 0;
        return (double)_metrics.CachedRequests / _metrics.TotalRequests;
    }

    private void AdaptForFailures()
    {
        // Увеличиваем retry delay
        var newDelay = Math.Min(_metrics.RetryDelayMs * RetryDelayMultiplier, MaxRetryDelayMs);
        if (newDelay > _metrics.RetryDelayMs)
        {
            _metrics.RetryDelayMs = (int)newDelay;
            LastChange = $"Увеличен retry delay: {_metrics.RetryDelayMs} мс (success_rate: {_metrics.SuccessRate:P0})";
            _logger.Warning($"Адаптация: success_rate {_metrics.SuccessRate:P0} < {FailureThreshold:P0}, увеличен retry delay до {_metrics.RetryDelayMs} мс");
        }

        // Увеличиваем retry count
        if (_metrics.RetryCount < MaxRetryCount)
        {
            _metrics.RetryCount += RetryCountIncrement;
            LastChange = $"Увеличен retry count: {_metrics.RetryCount} (success_rate: {_metrics.SuccessRate:P0})";
            _logger.Warning($"Адаптация: success_rate {_metrics.SuccessRate:P0} < {FailureThreshold:P0}, увеличен retry count до {_metrics.RetryCount}");
        }

        LastAdaptationTime = DateTime.UtcNow;
    }

    private void AdaptForCache()
    {
        var newSize = Math.Min((int)(_cache.Count * CacheSizeMultiplier), MaxCacheSize);
        if (newSize > _cache.Count)
        {
            // Примечание: RequestCache не имеет setter для maxSize, но мы можем логировать
            LastChange = $"Высокий cache hit rate: {CalculateCacheHitRate():P0}, рекомендуется увеличить maxSize";
            _logger.Info($"Адаптация: cache hit rate {CalculateCacheHitRate():P0} > {CacheHitRateThreshold:P0}, кэш эффективен");
            LastAdaptationTime = DateTime.UtcNow;
        }
    }

    private void AdaptForOptimization()
    {
        // Постепенно снижаем retry delay для оптимизации скорости
        var newDelay = Math.Max(_metrics.RetryDelayMs / 2, 500);
        if (newDelay < _metrics.RetryDelayMs)
        {
            _metrics.RetryDelayMs = newDelay;
            LastChange = $"Оптимизация: снижен retry delay до {_metrics.RetryDelayMs} мс (success_rate: {_metrics.SuccessRate:P0})";
            _logger.Info($"Адаптация: success_rate {_metrics.SuccessRate:P0} > 0.9, снижен retry delay до {_metrics.RetryDelayMs} мс");
        }

        // Снижаем retry count если он был увеличен
        if (_metrics.RetryCount > 2)
        {
            _metrics.RetryCount--;
            LastChange = $"Оптимизация: снижен retry count до {_metrics.RetryCount} (success_rate: {_metrics.SuccessRate:P0})";
            _logger.Info($"Адаптация: success_rate {_metrics.SuccessRate:P0} > 0.9, снижен retry count до {_metrics.RetryCount}");
        }

        LastAdaptationTime = DateTime.UtcNow;
    }
}
