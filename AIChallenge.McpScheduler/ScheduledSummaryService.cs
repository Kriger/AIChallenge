using System.Text.Json;

namespace AIChallenge.McpScheduler;

/// <summary>
/// Сервис для периодических сводок по задачам.
/// Делает снимки состояния задач, сохраняет их в JSON и возвращает агрегированный отчёт.
/// </summary>
public sealed class ScheduledSummaryService
{
    private readonly ScheduledSummaryStore _store;
    private readonly Action<string> _log;
    private readonly Func<Task<string>> _fetchTasks;
    private readonly LlmSummaryService? _llmService;

    /// <summary>
    /// Создаёт сервис без LLM (только diff-отчёты).
    /// </summary>
    /// <param name="baseDirectory">Каталог для хранения данных (memory/).</param>
    /// <param name="log">Делегат логирования.</param>
    /// <param name="fetchTasks">Функция для получения данных задач (JSON-строка).</param>
    public ScheduledSummaryService(string baseDirectory, Action<string> log, Func<Task<string>> fetchTasks)
        : this(baseDirectory, log, fetchTasks, llmService: null)
    {
    }

    /// <summary>
    /// Создаёт сервис с опциональным LLM для умных отчётов.
    /// </summary>
    /// <param name="baseDirectory">Каталог для хранения данных (memory/).</param>
    /// <param name="log">Делегат логирования.</param>
    /// <param name="fetchTasks">Функция для получения данных задач (JSON-строка).</param>
    /// <param name="llmService">Сервис для генерации LLM-summary (null = отключено).</param>
    public ScheduledSummaryService(string baseDirectory, Action<string> log, Func<Task<string>> fetchTasks, LlmSummaryService? llmService)
    {
        _log = log;
        _fetchTasks = fetchTasks;
        _store = new ScheduledSummaryStore(baseDirectory, "scheduled_summary");
        _llmService = llmService;
    }

    /// <summary>
    /// Делает снимок и сохраняет его.
    /// Если предыдущий снимок есть — возвращает diff-отчёт.
    /// Если снимков нет — возвращает первый снимок.
    /// </summary>
    public async Task<string> TakeSnapshotAsync()
    {
        _log("📸 Делаю снимок задач...");

        // 1. Получаем данные задач
        var rawTasks = await _fetchTasks();
        var snapshot = ParseSnapshot(rawTasks);

        // 2. Загружаем предыдущий снимок
        var previous = _store.GetLatest();

        // 3. Проверяем, изменилось ли что-то
        if (previous is not null)
        {
            var diff = ScheduledSummaryStore.ComputeDiff(previous, snapshot);
            var hasChanges = diff.NewTasks > 0 || diff.NewlyCompleted > 0 || diff.NewlyPending < 0;

            if (!hasChanges)
            {
                // Ничего не изменилось — возвращаем сообщение без вызова LLM
                return $"✅ Изменений нет.\n   Всего: {snapshot.TotalCount} | ✅ Выполнено: {snapshot.CompletedCount} | ⏳ Ожидает: {snapshot.PendingCount}";
            }
        }

        // 4. Сохраняем текущий снимок
        _store.SaveSnapshot(snapshot);

        // 5. Формируем отчёт
        if (_llmService is not null)
        {
            // LLM-режим: генерируем умный отчёт с учётом предыдущего
            var previousLlmSummary = _store.GetLastLlmSummary();
            string result;

            if (previous is null || previousLlmSummary is null)
            {
                // Первый запуск
                result = await _llmService.GenerateSummaryAsync(rawTasks);
            }
            else
            {
                // Сравнение с предыдущим LLM-отчётом
                result = await _llmService.GenerateDiffSummaryAsync(rawTasks, previousLlmSummary);
            }

            _store.SaveLlmSummary(result);
            return result;
        }

        // Обычный режим (без LLM)
        if (previous is null)
        {
            return FormatFirstSnapshot(snapshot);
        }
        else
        {
            var diff = ScheduledSummaryStore.ComputeDiff(previous, snapshot);
            return FormatDiffReport(diff, snapshot);
        }
    }

    /// <summary>
    /// Получает последний отчёт без нового снимка.
    /// </summary>
    public string GetLastReport()
    {
        var latest = _store.GetLatest();
        var previous = _store.GetAt(_store.GetLatest() is var l && l != null ? _store.GetLatest()!.Timestamp == l.Timestamp ? -1 : -1 : -1);

        if (latest is null)
            return "Нет сохранённых снимков. Используйте TakeSnapshotAsync() для создания первого.";

        if (previous is null)
            return FormatFirstSnapshot(latest);

        var diff = ScheduledSummaryStore.ComputeDiff(previous, latest);
        return FormatDiffReport(diff, latest);
    }

    /// <summary>
    /// Парсит JSON-строку задач в TaskSnapshot.
    /// </summary>
    private TaskSnapshot ParseSnapshot(string rawTasksJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawTasksJson);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
                throw new FormatException("Ожидается JSON-массив задач");

            var completedCount = 0;
            var pendingCount = 0;
            var completedTitles = new List<string>();
            var pendingTitles = new List<string>();
            var byProject = new Dictionary<string, int>();
            var byPriority = new Dictionary<string, int>();

            foreach (var task in root.EnumerateArray())
            {
                var title = ExtractString(task, "Title", "title", "Название", "name", "Subject", "subject", "TaskTitle");
                var isCompleted = ExtractString(task, "IsCompleted", "isCompleted", "СтатусВыполнения", "status", "Status", "Выполнена", "выполнена");
                var project = ExtractString(task, "ProjectTitle", "projectTitle", "Project", "project", "Проект", "проект", "НазваниеПроекта");
                var priority = ExtractString(task, "Priority", "priority", "Приоритет", "priority_ru");

                bool completed = !string.IsNullOrEmpty(isCompleted) &&
                    (isCompleted.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                     isCompleted.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                     isCompleted.Equals("выполнена", StringComparison.OrdinalIgnoreCase) ||
                     isCompleted.Equals("выполнено", StringComparison.OrdinalIgnoreCase) ||
                     isCompleted.Equals("completed", StringComparison.OrdinalIgnoreCase));

                if (completed)
                {
                    completedCount++;
                    if (!string.IsNullOrEmpty(title))
                        completedTitles.Add(title);
                }
                else
                {
                    pendingCount++;
                    if (!string.IsNullOrEmpty(title))
                        pendingTitles.Add(title);
                }

                // По проектам
                if (!string.IsNullOrEmpty(project))
                {
                    if (!byProject.TryGetValue(project, out var projCount))
                        byProject[project] = 0;
                    byProject[project]++;
                }

                // По приоритетам
                if (!string.IsNullOrEmpty(priority))
                {
                    if (!byPriority.TryGetValue(priority, out var priCount))
                        byPriority[priority] = 0;
                    byPriority[priority]++;
                }
            }

            return new TaskSnapshot(
                Timestamp: DateTime.Now,
                TotalCount: completedCount + pendingCount,
                CompletedCount: completedCount,
                PendingCount: pendingCount,
                ByProject: byProject,
                ByPriority: byPriority,
                CompletedTaskTitles: completedTitles,
                PendingTaskTitles: pendingTitles
            );
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Ошибка парсинга JSON задач: {ex.Message}");
        }
    }

    /// <summary>
    /// Форматирует первый снимок.
    /// </summary>
    private string FormatFirstSnapshot(TaskSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("📊 Первый снимок задач:");
        sb.AppendLine($"   Всего: {snapshot.TotalCount}");
        sb.AppendLine($"   ✅ Выполнено: {snapshot.CompletedCount} ({GetPercent(snapshot.CompletedCount, snapshot.TotalCount)}%)");
        sb.AppendLine($"   ⏳ Ожидает: {snapshot.PendingCount} ({GetPercent(snapshot.PendingCount, snapshot.TotalCount)}%)");
        sb.AppendLine();

        if (snapshot.ByProject.Count > 0)
        {
            sb.AppendLine("   По проектам:");
            foreach (var kvp in snapshot.ByProject.OrderBy(k => k.Key))
                sb.AppendLine($"     • {kvp.Key}: {kvp.Value}");
            sb.AppendLine();
        }

        if (snapshot.ByPriority.Count > 0)
        {
            sb.AppendLine("   По приоритетам:");
            foreach (var kvp in snapshot.ByPriority.OrderBy(k => k.Key))
                sb.AppendLine($"     • {kvp.Key}: {kvp.Value}");
            sb.AppendLine();
        }

        sb.AppendLine($"   Снимок сохранён в memory/scheduled_summary_snapshots.json");
        return sb.ToString();
    }

    /// <summary>
    /// Форматирует diff-отчёт.
    /// </summary>
    private string FormatDiffReport(SnapshotDiff diff, TaskSnapshot current)
    {
        var sb = new StringBuilder();
        sb.AppendLine("📊 Сводка изменений:");
        sb.AppendLine();

        // Общее количество
        sb.Append($"   Всего задач: {diff.TotalBefore} → {diff.TotalAfter}");
        if (diff.NewTasks > 0)
            sb.AppendLine($" (+{diff.NewTasks})");
        else if (diff.NewTasks < 0)
            sb.AppendLine($" ({diff.NewTasks})");
        else
            sb.AppendLine();

        // Выполнено
        sb.Append($"   ✅ Выполнено: {diff.CompletedBefore} → {diff.CompletedAfter}");
        if (diff.NewlyCompleted > 0)
            sb.AppendLine($" (+{diff.NewlyCompleted})");
        else if (diff.NewlyCompleted < 0)
            sb.AppendLine($" ({diff.NewlyCompleted})");
        else
            sb.AppendLine();

        // Ожидает
        var pendingNum = diff.TotalAfter - diff.CompletedAfter;
        sb.Append($"   ⏳ Ожидает: {pendingNum}");
        if (diff.NewlyPending != 0)
        {
            var sign = diff.NewlyPending > 0 ? "+" : "";
            sb.AppendLine($" ({sign}{diff.NewlyPending})");
        }
        else
            sb.AppendLine();

        sb.AppendLine();

        // Новые задачи
        if (diff.NewTaskTitles.Count > 0)
        {
            sb.AppendLine("   🆕 Новые задачи:");
            foreach (var title in diff.NewTaskTitles.Take(10))
                sb.AppendLine($"     • {title}");
            if (diff.NewTaskTitles.Count > 10)
                sb.AppendLine($"     ... и ещё {diff.NewTaskTitles.Count - 10}");
            sb.AppendLine();
        }

        // Только что выполненные
        if (diff.NewlyCompletedTitles.Count > 0)
        {
            sb.AppendLine("   ✅ Только что выполнено:");
            foreach (var title in diff.NewlyCompletedTitles.Take(10))
                sb.AppendLine($"     • {title}");
            if (diff.NewlyCompletedTitles.Count > 10)
                sb.AppendLine($"     ... и ещё {diff.NewlyCompletedTitles.Count - 10}");
            sb.AppendLine();
        }

        // Текущее состояние
        sb.AppendLine("   📈 Текущее состояние:");
        sb.AppendLine($"     Выполнено: {current.CompletedCount}/{current.TotalCount} ({GetPercent(current.CompletedCount, current.TotalCount)}%)");
        sb.AppendLine($"     Ожидает: {current.PendingCount}");

        if (current.ByProject.Count > 0)
        {
            sb.AppendLine("     По проектам:");
            foreach (var kvp in current.ByProject.OrderBy(k => k.Value, Comparer<int>.Create((a, b) => b - a)).Take(5))
                sb.AppendLine($"       • {kvp.Key}: {kvp.Value}");
        }

        return sb.ToString();
    }

    private static string? ExtractString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                return prop.ValueKind switch
                {
                    JsonValueKind.String => prop.GetString(),
                    JsonValueKind.Number => prop.GetInt32().ToString(),
                    JsonValueKind.True or JsonValueKind.False => prop.GetBoolean().ToString().ToLowerInvariant(),
                    JsonValueKind.Null => null,
                    _ => prop.ToString()
                };
            }
        }
        return null;
    }

    private static int GetPercent(int part, int total)
    {
        return total == 0 ? 0 : (int)((double)part / total * 100);
    }
}
