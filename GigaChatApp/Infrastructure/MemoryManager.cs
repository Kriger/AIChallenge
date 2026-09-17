namespace GigaChatApp.Infrastructure;

/// <summary>
/// Тип памяти — определяет, куда сохраняется или откуда загружается информация.
/// </summary>
public enum MemoryType
{
    /// <summary>Краткосрочная: текущий диалог, последние сообщения.</summary>
    ShortTerm,

    /// <summary>Рабочая: данные текущей задачи, план, промежуточные результаты.</summary>
    Working,

    /// <summary>Долгосрочная: знания, профиль, факты, решения.</summary>
    LongTerm,
}

/// <summary>
/// Менеджер памяти — единая точка управления тремя типами памяти агента.
/// Обеспечивает явный выбор, что и куда сохраняется.
/// </summary>
public class MemoryManager
{
    private readonly ShortTermMemory _shortTerm;
    private readonly WorkingMemory _working;
    private readonly LongTermMemory _longTerm;
    private readonly AgentLogger _logger;

    /// <summary>Краткосрочная память (текущий диалог).</summary>
    public ShortTermMemory ShortTerm => _shortTerm;

    /// <summary>Рабочая память (текущая задача).</summary>
    public WorkingMemory Working => _working;

    /// <summary>Долгосрочная память (знания, факты).</summary>
    public LongTermMemory LongTerm => _longTerm;

    public MemoryManager(AgentLogger? logger = null)
    {
        _logger = logger ?? new AgentLogger(LogLevel.Info);
        _shortTerm = new ShortTermMemory(_logger);
        _working = new WorkingMemory(_logger);
        _longTerm = new LongTermMemory(_logger);
    }

    /// <summary>
    /// Сохранить информацию в указанный тип памяти.
    /// Явный выбор места сохранения.
    /// </summary>
    /// <param name="type">Тип памяти: ShortTerm, Working, LongTerm.</param>
    /// <param name="key">Ключ данных.</param>
    /// <param name="value">Значение.</param>
    /// <param name="source">Источник (user, agent, system).</param>
    public void Save(MemoryType type, string key, string value, string source = "user")
    {
        switch (type)
        {
            case MemoryType.ShortTerm:
                _shortTerm.Add(source, value, key);
                break;
            case MemoryType.Working:
                _working.Save(key, value, source);
                break;
            case MemoryType.LongTerm:
                _longTerm.Save(key, value, source);
                break;
        }
    }

    /// <summary>
    /// Загрузить информацию из указанного типа памяти.
    /// </summary>
    /// <param name="type">Тип памяти.</param>
    /// <param name="key">Ключ для поиска.</param>
    public object? Load(MemoryType type, string key)
    {
        return type switch
        {
            MemoryType.ShortTerm => _shortTerm.Search(key),
            MemoryType.Working => _working.Load(key),
            MemoryType.LongTerm => _longTerm.Load(key),
            _ => null,
        };
    }

    /// <summary>
    /// Найти релевантную информацию в указанном типе памяти.
    /// </summary>
    /// <param name="type">Тип памяти.</param>
    /// <param name="query">Запрос для поиска.</param>
    public object? FindRelevant(MemoryType type, string query)
    {
        return type switch
        {
            MemoryType.ShortTerm => _shortTerm.Search(query),
            MemoryType.Working => _working.LoadByType(query),
            MemoryType.LongTerm => _longTerm.FindRelevant(query),
            _ => null,
        };
    }

    /// <summary>
    /// Добавить сообщение в краткосрочную память (диалог).
    /// </summary>
    public void AddToDialogue(string role, string content)
    {
        _shortTerm.Add(role, content);
    }

    /// <summary>
    /// Начать новую задачу (сбрасывает рабочую память).
    /// </summary>
    public void StartTask(string taskId)
    {
        _working.StartTask(taskId);
    }

    /// <summary>
    /// Завершить текущую задачу.
    /// </summary>
    public void CompleteTask(string? result = null)
    {
        _working.CompleteTask(result);
    }

    /// <summary>
    /// Провалить текущую задачу.
    /// </summary>
    public void FailTask(string reason)
    {
        _working.FailTask(reason);
    }

    /// <summary>
    /// Получить сводку по всем типам памяти.
    /// </summary>
    public string GetStatus()
    {
        return $"""
            === Статус памяти ===
            Краткосрочная (диалог): {_shortTerm.Count} записей
            Рабочая (задача):       {_working.Count} записей, задача: {_working.CurrentTaskId ?? "нет"} [{_working.CurrentTaskStatus ?? "нет"}]
            Долгосрочная (знания):  {_longTerm.Count} фактов
            =====================
            """;
    }

    /// <summary>
    /// Очистить все типы памяти.
    /// </summary>
    public void ClearAll()
    {
        _shortTerm.Clear();
        _working.Clear();
        _longTerm.Clear();
        _logger.Info("Все типы памяти очищены");
    }

    /// <summary>
    /// Очистить только краткосрочную память (диалог).
    /// </summary>
    public void ClearShortTerm()
    {
        _shortTerm.Clear();
    }

    /// <summary>
    /// Очистить только рабочую память (задача).
    /// </summary>
    public void ClearWorking()
    {
        _working.Clear();
    }

    /// <summary>
    /// Очистить только долгосрочную память (факты).
    /// </summary>
    public void ClearLongTerm()
    {
        _longTerm.Clear();
    }
}
