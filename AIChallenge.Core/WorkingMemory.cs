using AIChallenge.Models;

namespace AIChallenge.Core.Infrastructure;

/// <summary>
/// Рабочая память — данные текущей задачи.
/// Хранит план, промежуточные результаты, временные данные.
/// Очищается при завершении задачи или переключении на новую.
/// Поддерживает архивацию завершённых задач.
/// </summary>
public class WorkingMemory
{
    private readonly Dictionary<string, WorkingEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<WorkingEntry>> _archivedTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly AgentLogger _logger;

    /// <summary>
    /// Максимальное количество архивных записей на задачу.
    /// При превышении oldest архивные записи удаляются.
    /// </summary>
    public int MaxArchiveSize { get; set; } = 10;

    /// <summary>
    /// Включить автоматическую архивацию при StartTask.
    /// </summary>
    public bool ArchiveEnabled { get; set; } = true;

    /// <summary>
    /// Идентификатор текущей задачи (сессии).
    /// </summary>
    public string? CurrentTaskId { get; set; }

    /// <summary>
    /// Статус текущей задачи.
    /// </summary>
    public string? CurrentTaskStatus { get; set; }

    /// <summary>
    /// Время начала текущей задачи.
    /// </summary>
    public DateTime? TaskStartedAt { get; set; }

    public WorkingMemory(AgentLogger? logger = null)
    {
        _logger = logger ?? new AgentLogger(LogLevel.Info);
    }

    /// <summary>
    /// Начать новую задачу. Очищает рабочую память от данных предыдущей задачи.
    /// Если ArchiveEnabled = true, данные предыдущей задачи архивируются.
    /// </summary>
    public void StartTask(string taskId)
    {
        // Архивируем текущие данные перед очисткой
        if (ArchiveEnabled && _entries.Count > 0 && CurrentTaskId is not null)
        {
            ArchiveCurrentTask();
        }

        _entries.Clear();
        CurrentTaskId = taskId;
        CurrentTaskStatus = "running";
        TaskStartedAt = DateTime.UtcNow;
        _logger.Info($"[Рабоч. память] Начата задача: {taskId}");
    }

    /// <summary>
    /// Архивировать данные текущей задачи.
    /// </summary>
    public void ArchiveCurrentTask()
    {
        if (CurrentTaskId is null || _entries.Count == 0)
            return;

        var currentData = _entries.Values.ToList();

        if (!_archivedTasks.ContainsKey(CurrentTaskId))
        {
            _archivedTasks[CurrentTaskId] = new List<WorkingEntry>();
        }

        _archivedTasks[CurrentTaskId].AddRange(currentData);

        // Ограничиваем размер архива
        if (_archivedTasks[CurrentTaskId].Count > MaxArchiveSize)
        {
            var removed = _archivedTasks[CurrentTaskId].Count - MaxArchiveSize;
            _archivedTasks[CurrentTaskId].RemoveRange(0, removed);
            _logger.Info($"[Рабоч. память] Архив задачи {CurrentTaskId}: удалено {removed} старых записей");
        }

        _logger.Info($"[Рабоч. память] Архивирована задача {CurrentTaskId}: {_entries.Count} записей");
    }

    /// <summary>
    /// Загрузить архивные данные задачи.
    /// </summary>
    public IReadOnlyList<WorkingEntry> GetArchivedTask(string taskId)
    {
        if (_archivedTasks.TryGetValue(taskId, out var archived))
        {
            return archived.AsReadOnly();
        }
        return Array.Empty<WorkingEntry>();
    }

    /// <summary>
    /// Получить все архивированные задачи.
    /// </summary>
    public IReadOnlyDictionary<string, List<WorkingEntry>> AllArchived => _archivedTasks;

    /// <summary>
    /// Очистить архив.
    /// </summary>
    public void ClearArchive()
    {
        var count = _archivedTasks.Count;
        _archivedTasks.Clear();
        _logger.Info($"[Рабоч. память] Архив очищен: {count} задач");
    }

    /// <summary>
    /// Завершить текущую задачу.
    /// </summary>
    public void CompleteTask(string? result = null)
    {
        CurrentTaskStatus = result is null ? "completed" : "completed_with_result";
        _logger.Info($"[Рабоч. память] Задача {CurrentTaskId} завершена");
    }

    /// <summary>
    /// Провалить текущую задачу.
    /// </summary>
    public void FailTask(string reason)
    {
        CurrentTaskStatus = "failed";
        _logger.Info($"[Рабоч. память] Задача {CurrentTaskId} провалена: {reason}");
    }

    /// <summary>
    /// Сохранить данные в рабочую память.
    /// </summary>
    public void Save(string key, string value, string type = "data")
    {
        var wasNew = !_entries.ContainsKey(key);

        if (wasNew)
        {
            _entries[key] = new WorkingEntry
            {
                Key = key,
                Value = value,
                Type = type,
                TaskId = CurrentTaskId,
                CreatedAt = DateTime.UtcNow,
            };
            _logger.Info($"[Рабоч. память] Сохранено: {key} = \"{Truncate(value, 50)}\" (тип: {type}, задача: {CurrentTaskId ?? "нет"})");
        }
        else
        {
            _entries[key].Value = value;
            _entries[key].Type = type;
            _entries[key].TaskId = CurrentTaskId;
            _entries[key].UpdatedAt = DateTime.UtcNow;
            _logger.Info($"[Рабоч. память] Обновлено: {key} = \"{Truncate(value, 50)}\" (задача: {CurrentTaskId ?? "нет"})");
        }
    }

    /// <summary>
    /// Сохранить план в рабочую память.
    /// </summary>
    public void SavePlan(Plan plan)
    {
        // Устанавливаем текущую задачу
        if (CurrentTaskId is null)
        {
            CurrentTaskId = $"plan-{DateTime.UtcNow:yyyyMMddHHmmss}";
            CurrentTaskStatus = "running";
            TaskStartedAt = DateTime.UtcNow;
        }

        Save("plan", plan.OriginalRequest, "plan");
        Save("plan_status", plan.StatusText, "plan");

        for (int i = 0; i < plan.Tasks.Count; i++)
        {
            var task = plan.Tasks[i];
            Save($"task.{i}.description", task.Description, "task");
            Save($"task.{i}.context", task.Context, "task");
            Save($"task.{i}.result", task.Result, "task");
            Save($"task.{i}.status", task.StatusText, "task");
        }

        if (plan.FinalAnswer is not null)
        {
            Save("plan.final_answer", plan.FinalAnswer, "plan");
        }

        _logger.Info($"[Рабоч. память] План сохранён: {plan.OriginalRequest}");
    }

    /// <summary>
    /// Загрузить данные по ключу.
    /// </summary>
    public WorkingEntry? Load(string key)
    {
        return _entries.TryGetValue(key, out var entry) ? entry : null;
    }

    /// <summary>
    /// Загрузить все данные указанного типа.
    /// </summary>
    public IReadOnlyList<WorkingEntry> LoadByType(string type)
    {
        return _entries.Values
            .Where(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Загрузить все данные текущей задачи.
    /// Если задача не начата — возвращает все записи.
    /// </summary>
    public IReadOnlyList<WorkingEntry> LoadCurrentTaskData()
    {
        if (CurrentTaskId is null)
            return _entries.Values.ToList().AsReadOnly();

        return _entries.Values
            .Where(e => e.TaskId == CurrentTaskId)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Удалить запись.
    /// </summary>
    public bool Delete(string key)
    {
        var removed = _entries.Remove(key, out _);
        if (removed)
            _logger.Info($"[Рабоч. память] Удалено: {key}");
        return removed;
    }

    /// <summary>
    /// Очистить всю рабочую память.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        CurrentTaskId = null;
        CurrentTaskStatus = null;
        TaskStartedAt = null;
        _logger.Info("[Рабоч. память] Память очищена");
    }

    /// <summary>
    /// Все записи.
    /// </summary>
    public IReadOnlyDictionary<string, WorkingEntry> All => _entries;

    /// <summary>
    /// Количество записей.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Количество архивированных задач.
    /// </summary>
    public int ArchivedTaskCount => _archivedTasks.Count;

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 3)] + "...";
    }
}

/// <summary>
/// Запись рабочей памяти.
/// </summary>
public class WorkingEntry
{
    /// <summary>Ключ данных.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Значение.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Тип данных (plan, task, data, result).</summary>
    public string Type { get; set; } = "data";

    /// <summary>Идентификатор задачи, к которой относится запись.</summary>
    public string? TaskId { get; set; }

    /// <summary>Время создания.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Время последнего обновления.</summary>
    public DateTime? UpdatedAt { get; set; }
}
