using AIChallenge.Models;

namespace AIChallenge.Core.Infrastructure;

/// <summary>
/// Долгосрочная память агента.
/// Хранит факты, знания, профиль пользователя и принятые решения.
/// Переживает сессию диалога, сохраняется между запусками.
/// </summary>
public class LongTermMemory
{
    private readonly Dictionary<string, Fact> _facts = new(StringComparer.OrdinalIgnoreCase);
    private readonly AgentLogger _logger;

    /// <summary>
    /// Максимальное количество фактов в памяти.
    /// </summary>
    public int MaxSize { get; set; } = 500;

    public LongTermMemory(AgentLogger? logger = null)
    {
        _logger = logger ?? new AgentLogger(LogLevel.Info);
    }

    /// <summary>
    /// Сохранить факт. Если ключ уже существует — обновляет значение.
    /// </summary>
    /// <param name="key">Ключ факта.</param>
    /// <param name="value">Значение факта.</param>
    /// <param name="source">Источник: user, extracted, explicit, migrated.</param>
    public void Save(string key, string value, string source = "user")
    {
        Save(key, value, source, FactPriority.Normal);
    }

    /// <summary>
    /// Сохранить факт с указанием приоритета.
    /// Факты с Priority.Critical никогда не удаляются при eviction.
    /// </summary>
    /// <param name="key">Ключ факта.</param>
    /// <param name="value">Значение факта.</param>
    /// <param name="source">Источник.</param>
    /// <param name="priority">Приоритет факта.</param>
    public void Save(string key, string value, string source, FactPriority priority)
    {
        var wasNew = !_facts.ContainsKey(key);

        if (_facts.TryGetValue(key, out var existing))
        {
            existing.Value = value;
            existing.Source = source;
            if (priority != FactPriority.Normal)
                existing.Priority = priority;
            existing.CreatedAt = DateTime.UtcNow;
            _logger.Info($"[Длг. память] Факт обновлён: {key} = \"{Truncate(value, 50)}\"");
        }
        else
        {
            // Если память переполнена — удаляем самый старый факт
            // Приоритет Critical не удаляется
            if (_facts.Count >= MaxSize)
            {
                var victim = FindEvictionVictim();
                if (victim.Key is not null)
                {
                    _facts.Remove(victim.Key);
                    _logger.Info($"[Длг. память] Память переполнена, удалён факт: {victim.Key} (приоритет: {victim.Value.Priority})");
                }
            }

            _facts[key] = new Fact
            {
                Key = key,
                Value = value,
                Source = source,
                Priority = priority,
                CreatedAt = DateTime.UtcNow,
            };
            _logger.Info($"[Длг. память] Факт сохранён: {key} = \"{Truncate(value, 50)}\" [приоритет: {priority}]");
        }

        // Сохраняем информацию о последнем изменении
        _lastChanges = new List<FactChange>
        {
            new() { Key = key, WasNew = wasNew, Value = value, Source = source, Priority = priority }
        };
    }

    /// <summary>
    /// Находит факт для удаления (eviction).
    /// Приоритет Critical пропускается — он никогда не удаляется.
    /// Выбирается самый старый факт с наименьшим приоритетом.
    /// </summary>
    private (Fact Value, string? Key) FindEvictionVictim()
    {
        // Сортируем: сначала Critical (protected), затем по приоритету ASC, затем по CreatedAt ASC
        var candidates = _facts
            .Where(f => f.Value.Priority != FactPriority.Critical)
            .OrderBy(f => f.Value.Priority)
            .ThenBy(f => f.Value.CreatedAt)
            .ToList();

        if (candidates.Count == 0)
        {
            // Все факты Critical — возвращаем самый старый (крайний случай)
            var oldest = _facts.OrderBy(f => f.Value.CreatedAt).FirstOrDefault();
            return (oldest.Value, oldest.Key);
        }

        return (candidates[0].Value, candidates[0].Key);
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
        public FactPriority Priority { get; set; }
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

            // Бонус за приоритет факта
            score += (int)fact.Priority * 20;

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
        _logger.Info("[Длг. память] Память очищена");
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
