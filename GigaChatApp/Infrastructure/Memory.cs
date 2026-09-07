using GigaChatApp.Models;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Долгосрочная память агента.
/// Хранит факты, позволяет искать релевантные по ключу или содержимому.
/// </summary>
public class Memory
{
    private readonly Dictionary<string, Fact> _facts = new(StringComparer.OrdinalIgnoreCase);
    private readonly AgentLogger _logger;

    /// <summary>
    /// Максимальное количество фактов в памяти.
    /// </summary>
    public int MaxSize { get; set; } = 500;

    public Memory(AgentLogger? logger = null)
    {
        _logger = logger ?? new AgentLogger(LogLevel.Info);
    }

    /// <summary>
    /// Сохранить факт. Если ключ уже существует — обновляет значение.
    /// </summary>
    public void Save(string key, string value, string source = "user")
    {
        var wasNew = !_facts.ContainsKey(key);
        
        if (_facts.TryGetValue(key, out var existing))
        {
            existing.Value = value;
            existing.Source = source;
            existing.CreatedAt = DateTime.UtcNow;
            _logger.Info($"Факт обновлён: {key} = \"{Truncate(value, 50)}\"");
        }
        else
        {
            // Если память переполнена — удаляем самый старый факт
            if (_facts.Count >= MaxSize)
            {
                var oldest = _facts.OrderBy(f => f.Value.CreatedAt).FirstOrDefault();
                if (oldest.Key is not null)
                {
                    _facts.Remove(oldest.Key);
                    _logger.Info($"Память переполнена, удалён факт: {oldest.Key}");
                }
            }

            _facts[key] = new Fact
            {
                Key = key,
                Value = value,
                Source = source,
                CreatedAt = DateTime.UtcNow,
            };
            _logger.Info($"Факт сохранён: {key} = \"{Truncate(value, 50)}\"");
        }
        
        // Сохраняем информацию о последнем изменении
        _lastChanges = new List<FactChange>
        {
            new() { Key = key, WasNew = wasNew, Value = value, Source = source }
        };
    }

    /// <summary>
    /// Получить список изменений памяти за последнее действие.
    /// </summary>
    public IReadOnlyList<FactChange> GetRecentChanges()
    {
        return _lastChanges ?? (IReadOnlyList<FactChange>)Array.Empty<FactChange>();
    }

    private List<FactChange>? _lastChanges;

    /// <summary>
    /// Информация об изменении факта.
    /// </summary>
    public class FactChange
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public bool WasNew { get; set; }
    }

    /// <summary>
    /// Загрузить факт по ключу.
    /// </summary>
    public Fact? Load(string key)
    {
        if (_facts.TryGetValue(key, out var fact))
        {
            fact.LastReadAt = DateTime.UtcNow;
            fact.ReadCount++;
            return fact;
        }
        return null;
    }

    /// <summary>
    /// Найти релевантные факты для данного запроса.
    /// Ищет по ключу и содержимому.
    /// </summary>
    public List<Fact> FindRelevant(string query)
    {
        var queryLower = query.ToLowerInvariant();
        var queryWords = queryLower.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries);

        var scored = new List<(Fact Fact, int Score)>();

        foreach (var kvp in _facts)
        {
            var fact = kvp.Value;
            var score = 0;

            // Точное совпадение ключа — максимальный балл
            if (fact.Key.Equals(queryLower, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }
            // Частичное совпадение ключа
            else if (fact.Key.Contains(queryLower) || queryLower.Contains(fact.Key))
            {
                score += 50;
            }

            // Совпадение по словам
            foreach (var word in queryWords)
            {
                if (word.Length < 3) continue;

                // Слово есть в значении
                if (fact.Value.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }

                // Слово есть в ключе
                if (fact.Key.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                }
            }

            // Бонус за частоту использования (факт уже был релевантен раньше)
            score += fact.ReadCount * 2;

            if (score > 0)
            {
                scored.Add((fact, score));
            }
        }

        // Сортируем по релевантности и берём топ-N
        return scored.OrderByDescending(x => x.Score).Take(10).Select(x => x.Fact).ToList();
    }

    /// <summary>
    /// Удалить факт по ключу.
    /// </summary>
    public bool Delete(string key)
    {
        return _facts.Remove(key, out _);
    }

    /// <summary>
    /// Удалить все факты.
    /// </summary>
    public void Clear()
    {
        _facts.Clear();
        _logger.Info("Память очищена");
    }

    /// <summary>
    /// Все факты.
    /// </summary>
    public IReadOnlyDictionary<string, Fact> All => _facts;

    /// <summary>
    /// Количество фактов.
    /// </summary>
    public int Count => _facts.Count;

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 3)] + "...";
    }
}
