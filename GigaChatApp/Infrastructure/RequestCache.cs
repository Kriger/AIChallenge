using System.Collections.Concurrent;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Кэш ответов на запросы с fuzzy-поиском похожих.
/// </summary>
public class RequestCache
{
    private readonly ConcurrentDictionary<string, CachedEntry> _cache = new();
    private readonly int _maxSize;

    /// <summary>
    /// Создаёт кэш с заданным максимальным размером.
    /// </summary>
    public RequestCache(int maxSize = 100)
    {
        _maxSize = maxSize;
    }

    /// <summary>
    /// Получить ответ из кэша по тексту запроса (fuzzy-поиск).
    /// </summary>
    public string? TryGet(string query)
    {
        // Сначала точное совпадение
        if (_cache.TryGetValue(query, out var exact))
        {
            if (exact.ExpiresAt > DateTime.UtcNow)
            {
                return exact.Answer;
            }

            // Просроченный — удаляем
            _cache.TryRemove(query, out _);
        }

        // Fuzzy-поиск: ищем похожий запрос по левому префиксу (минимум 4 символа)
        if (query.Length >= 4)
        {
            var prefix = query[..4].ToLowerInvariant();
            var bestMatch = default((string key, CachedEntry entry, int commonPrefix));

            foreach (var kvp in _cache)
            {
                var normalized = kvp.Key.ToLowerInvariant();
                if (!normalized.StartsWith(prefix))
                    continue;

                var common = CommonPrefixLength(normalized, query.ToLowerInvariant());
                if (common >= 4 && bestMatch.commonPrefix < common)
                {
                    bestMatch = (kvp.Key, kvp.Value, common);
                }
            }

            if (bestMatch.entry is not null && bestMatch.entry.ExpiresAt > DateTime.UtcNow)
            {
                return bestMatch.entry.Answer;
            }
        }

        return null;
    }

    /// <summary>
    /// Сохранить ответ в кэш.
    /// </summary>
    public void Add(string query, string answer)
    {
        // Если кэш переполнен — удаляем самый старый
        if (_cache.Count >= _maxSize)
        {
            var oldest = _cache.OrderBy(kvp => kvp.Value.AddedAt).FirstOrDefault();
            if (oldest.Key is not null)
                _cache.TryRemove(oldest.Key, out _);
        }

        _cache[query] = new CachedEntry
        {
            Answer = answer,
            AddedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        };
    }

    /// <summary>
    /// Очистить кэш.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Количество записей в кэше.
    /// </summary>
    public int Count => _cache.Count;

    private static int CommonPrefixLength(string a, string b)
    {
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            if (a[i] != b[i])
                return i;
        }
        return len;
    }
}
