using System.Collections.Generic;
using GigaChatApp.Models;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Краткосрочная память — текущий диалог.
/// Хранит сообщения текущего разговора с автоматическим удалением старых при переполнении.
/// Поддерживает decay (затухание) для старых записей.
/// </summary>
public class ShortTermMemory
{
    private readonly List<MemoryEntry> _entries = new();
    private readonly AgentLogger _logger;

    /// <summary>
    /// Максимальное количество записей в краткосрочной памяти.
    /// При превышении oldest записи удаляются.
    /// </summary>
    public int MaxSize { get; set; } = 100;

    /// <summary>
    /// Включить decay (затухание) — автоматически помечать старые записи как "затухшие".
    /// Затухшие записи не удаляются, но помечаются флагом IsDecayed.
    /// </summary>
    public bool DecayEnabled { get; set; } = false;

    /// <summary>
    /// Минимальный возраст записи в часах для применения decay.
    /// По умолчанию 1 час.
    /// </summary>
    public int DecayAfterHours { get; set; } = 1;

    public ShortTermMemory(AgentLogger? logger = null)
    {
        _logger = logger ?? new AgentLogger(LogLevel.Info);
    }

    /// <summary>
    /// Применить decay к старым записям.
    /// Помечает записи старше DecayAfterHours как IsDecayed = true.
    /// Возвращает количество помеченных записей.
    /// </summary>
    public int ApplyDecay()
    {
        if (!DecayEnabled) return 0;

        var cutoff = DateTime.UtcNow.AddHours(-DecayAfterHours);
        int decayed = 0;

        foreach (var entry in _entries)
        {
            if (!entry.IsDecayed && entry.Timestamp < cutoff)
            {
                entry.IsDecayed = true;
                decayed++;
            }
        }

        if (decayed > 0)
        {
            _logger.Info($"[Кратк. память] Decay: помечено {decayed} записей как затухшие");
        }

        return decayed;
    }

    /// <summary>
    /// Удалить все затухшие записи.
    /// Возвращает количество удалённых записей.
    /// </summary>
    public int RemoveDecayed()
    {
        int removed = _entries.RemoveAll(e => e.IsDecayed);
        if (removed > 0)
        {
            _logger.Info($"[Кратк. память] Удалено {removed} затухших записей");
        }
        return removed;
    }

    /// <summary>
    /// Добавить запись в краткосрочную память.
    /// Если память переполнена — сначала удаляет затухшие записи, затем самые старые.
    /// </summary>
    public void Add(string role, string content, string? metadata = null)
    {
        var entry = new MemoryEntry
        {
            Role = role,
            Content = content,
            Metadata = metadata,
            Timestamp = DateTime.UtcNow,
        };

        _entries.Add(entry);

        // Удаляем старые записи при переполнении
        while (_entries.Count > MaxSize)
        {
            MemoryEntry? removed;

            // Сначала удаляем затухшие записи
            if (DecayEnabled)
            {
                var decayedIndex = _entries.FindIndex(e => e.IsDecayed);
                if (decayedIndex >= 0)
                {
                    removed = _entries[decayedIndex];
                    _entries.RemoveAt(decayedIndex);
                    _logger.Info($"[Кратк. память] Удалена затухшая запись: [{removed.Role}] {Truncate(removed.Content, 40)}");
                }
                else
                {
                    removed = _entries[0];
                    _entries.RemoveAt(0);
                    _logger.Info($"[Кратк. память] Удалена старая запись: [{removed.Role}] {Truncate(removed.Content, 40)}");
                }
            }
            else
            {
                removed = _entries[0];
                _entries.RemoveAt(0);
                _logger.Info($"[Кратк. память] Удалена старая запись: [{removed.Role}] {Truncate(removed.Content, 40)}");
            }
        }

        _logger.Info($"[Кратк. память] Добавлена запись: [{role}] {Truncate(content, 60)}");
    }

    /// <summary>
    /// Добавить сообщение ApiMessage.
    /// </summary>
    public void Add(ApiMessage message)
    {
        Add(message.Role, message.Content, message.Role);
    }

    /// <summary>
    /// Загрузить все записи.
    /// </summary>
    public IReadOnlyList<MemoryEntry> GetAll() => _entries.AsReadOnly();

    /// <summary>
    /// Загрузить последние N записей.
    /// </summary>
    public IReadOnlyList<MemoryEntry> GetRecent(int count)
    {
        var start = Math.Max(0, _entries.Count - count);
        return _entries.GetRange(start, Math.Min(count, _entries.Count)).AsReadOnly();
    }

    /// <summary>
    /// Найти записи по содержимому (частичное совпадение).
    /// </summary>
    public List<MemoryEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAll().ToList();

        var queryLower = query.ToLowerInvariant();
        return _entries
            .Where(e => e.Content.ToLowerInvariant().Contains(queryLower) ||
                        (e.Metadata != null && e.Metadata.ToLowerInvariant().Contains(queryLower)))
            .ToList();
    }

    /// <summary>
    /// Получить записи за последний промежуток времени.
    /// </summary>
    public IReadOnlyList<MemoryEntry> GetSince(DateTime from)
    {
        return _entries.Where(e => e.Timestamp >= from).ToList().AsReadOnly();
    }

    /// <summary>
    /// Удалить все записи.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        _logger.Info("[Кратк. память] Память очищена");
    }

    /// <summary>
    /// Количество записей.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Последняя запись в памяти.
    /// </summary>
    public MemoryEntry? Last => _entries.Count > 0 ? _entries[^1] : null;

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 3)] + "...";
    }
}

/// <summary>
/// Запись краткосрочной памяти.
/// </summary>
public class MemoryEntry
{
    /// <summary>Роль отправителя (user, assistant, system).</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Содержимое сообщения.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Дополнительные метаданные.</summary>
    public string? Metadata { get; set; }

    /// <summary>Время добавления.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Помечена ли запись как "затухшая" (старая, менее релевантная).
    /// Применяется при включённом decay.
    /// </summary>
    public bool IsDecayed { get; set; }
}
