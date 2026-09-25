using AIChallenge.McpScheduler;
using System.Text;
using System.Text.Json;

namespace AIChallenge.McpPipeline.Tools;

/// <summary>
/// Инструмент summarize — обработка данных и генерация отчёта.
/// Принимает JSON-массив задач и возвращает сводку.
/// Режимы: stats (статистика), diff (сравнение со снимком), llm (умный отчёт).
/// </summary>
public sealed class SummarizeTool : IDisposable
{
    private readonly ScheduledSummaryService? _summaryService;
    private readonly LlmSummaryService? _llmService;
    private readonly Action<string> _log;
    private bool _disposed;
    private readonly string _baseDirectory;

    public string Name => "summarize";
    public string Description => "Обработка задач и генерация отчёта. Режимы: stats, diff, llm.";

    public SummarizeTool(string? baseDirectory = null, Action<string>? log = null,
        LlmSummaryService? llmService = null)
    {
        _baseDirectory = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
        _log = log ?? (msg => Console.WriteLine($"  [summarize] {msg}"));
        _llmService = llmService;

        try
        {
            _summaryService = new ScheduledSummaryService(
                _baseDirectory,
                msg => _log(msg),
                () => Task.FromResult("[]")
            );
        }
        catch (Exception ex)
        {
            _log($"⚠️ Не удалось создать ScheduledSummaryService: {ex.Message}");
        }
    }

    /// <summary>
    /// Выполняет суммаризацию.
    /// Параметры: tasksJson (string), mode (string? = "stats"), previousSummary (string?)
    /// Возвращает: отчёт (string).
    /// </summary>
    public async Task<string> ExecuteAsync(Dictionary<string, object?> parameters)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SummarizeTool));

        // Получаем JSON задач
        if (!parameters.TryGetValue("tasksJson", out var tasksJsonObj) || tasksJsonObj == null)
        {
            return "{\"error\": \"Отсутствует параметр tasksJson\"}";
        }

        var tasksJson = tasksJsonObj.ToString() ?? "[]";
        var mode = parameters.TryGetValue("mode", out var modeObj)
            ? (modeObj.ToString()?.ToLowerInvariant() ?? "stats")
            : "stats";
        var previousSummary = parameters.TryGetValue("previousSummary", out var prevObj)
            ? prevObj?.ToString()
            : null;

        _log($"📊 Суммаризация в режиме: {mode}");

        return mode switch
        {
            "llm" when _llmService != null => await SummarizeWithLlmAsync(tasksJson, previousSummary),
            "llm" => await SummarizeWithLlmFallbackAsync(tasksJson),
            "diff" => await SummarizeDiffAsync(tasksJson),
            "stats" or _ => SummarizeStatsAsync(tasksJson),
        };
    }

    /// <summary>
    /// LLM-суммаризация через GigaChat.
    /// </summary>
    private async Task<string> SummarizeWithLlmAsync(string tasksJson, string? previousSummary)
    {
        try
        {
            string result;
            if (previousSummary != null)
            {
                result = await _llmService!.GenerateDiffSummaryAsync(tasksJson, previousSummary);
                _log("✅ LLM-отчёт с учётом предыдущего состояния");
            }
            else
            {
                result = await _llmService!.GenerateSummaryAsync(tasksJson);
                _log("✅ LLM-отчёт (первый запуск)");
            }
            return result;
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка LLM: {ex.Message}");
            return $"{{\"error\": \"Ошибка LLM: {ex.Message}\"}}";
        }
    }

    /// <summary>
    /// Fallback для LLM — используем ScheduledSummaryService.
    /// </summary>
    private async Task<string> SummarizeWithLlmFallbackAsync(string tasksJson)
    {
        try
        {
            if (_summaryService != null)
            {
                var result = await _summaryService.TakeSnapshotAsync();
                _log("✅ Отчёт через ScheduledSummaryService");
                return result;
            }
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка fallback: {ex.Message}");
        }

        return GenerateBasicSummary(tasksJson);
    }

    /// <summary>
    /// Diff-суммаризация — сравнение с предыдущим снимком.
    /// </summary>
    private async Task<string> SummarizeDiffAsync(string tasksJson)
    {
        try
        {
            if (_summaryService != null)
            {
                var result = await _summaryService.TakeSnapshotAsync();
                _log("✅ Diff-отчёт");
                return result;
            }
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка diff: {ex.Message}");
        }

        return GenerateBasicSummary(tasksJson);
    }

    /// <summary>
    /// Статистическая суммаризация — без вызова внешних сервисов.
    /// </summary>
    private string SummarizeStatsAsync(string tasksJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(tasksJson);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                return "{\"error\": \"Ожидается JSON-массив\"}";
            }

            var totalCount = 0;
            var completedCount = 0;
            var pendingCount = 0;
            var byProject = new Dictionary<string, int>();
            var byPriority = new Dictionary<string, int>();
            var completedTitles = new List<string>();
            var pendingTitles = new List<string>();

            foreach (var task in root.EnumerateArray())
            {
                totalCount++;

                var title = ExtractString(task, "Title", "title", "Название", "name", "Subject");
                var isCompleted = ExtractString(task, "IsCompleted", "isCompleted", "Status", "status", "Статус");
                var project = ExtractString(task, "ProjectTitle", "projectTitle", "Project", "project");
                var priority = ExtractString(task, "Priority", "priority");

                bool completed = !string.IsNullOrEmpty(isCompleted) &&
                    (isCompleted.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                     isCompleted.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                     isCompleted.Equals("выполнена", StringComparison.OrdinalIgnoreCase) ||
                     isCompleted.Equals("выполнено", StringComparison.OrdinalIgnoreCase) ||
                     isCompleted.Equals("completed", StringComparison.OrdinalIgnoreCase));

                if (completed)
                {
                    completedCount++;
                    if (!string.IsNullOrEmpty(title)) completedTitles.Add(title);
                }
                else
                {
                    pendingCount++;
                    if (!string.IsNullOrEmpty(title)) pendingTitles.Add(title);
                }

                if (!string.IsNullOrEmpty(project))
                {
                    if (!byProject.TryGetValue(project, out var c)) byProject[project] = 0;
                    byProject[project]++;
                }

                if (!string.IsNullOrEmpty(priority))
                {
                    if (!byPriority.TryGetValue(priority, out var p)) byPriority[priority] = 0;
                    byPriority[priority]++;
                }
            }

            var percent = totalCount > 0 ? (int)((double)completedCount / totalCount * 100) : 0;

            var sb = new StringBuilder();
            sb.AppendLine("📊 Сводка задач:");
            sb.AppendLine($"   Всего: {totalCount}");
            sb.AppendLine($"   ✅ Выполнено: {completedCount} ({percent}%)");
            sb.AppendLine($"   ⏳ Ожидает: {pendingCount}");
            sb.AppendLine();

            if (byProject.Count > 0)
            {
                sb.AppendLine("   По проектам:");
                foreach (var kvp in byProject.OrderBy(k => k.Key))
                    sb.AppendLine($"     • {kvp.Key}: {kvp.Value}");
                sb.AppendLine();
            }

            if (byPriority.Count > 0)
            {
                sb.AppendLine("   По приоритетам:");
                foreach (var kvp in byPriority.OrderBy(k => k.Key))
                    sb.AppendLine($"     • {kvp.Key}: {kvp.Value}");
                sb.AppendLine();
            }

            if (completedTitles.Count > 0)
            {
                sb.AppendLine("   ✅ Выполненные:");
                foreach (var t in completedTitles.Take(10))
                    sb.AppendLine($"     • {t}");
                if (completedTitles.Count > 10)
                    sb.AppendLine($"     ... и ещё {completedTitles.Count - 10}");
                sb.AppendLine();
            }

            if (pendingTitles.Count > 0)
            {
                sb.AppendLine("   ⏳ Ожидающие:");
                foreach (var t in pendingTitles.Take(10))
                    sb.AppendLine($"     • {t}");
                if (pendingTitles.Count > 10)
                    sb.AppendLine($"     ... и ещё {pendingTitles.Count - 10}");
            }

            _log("✅ Статистика сформирована");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка парсинга: {ex.Message}");
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }

    private static string GenerateBasicSummary(string tasksJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(tasksJson);
            var root = doc.RootElement;
            var count = root.ValueKind == JsonValueKind.Array ? root.GetArrayLength() : 0;
            return $"📊 Всего задач: {count}\n   Статистика сгенерирована.";
        }
        catch
        {
            return $"📊 Не удалось обработать данные.\n   Raw: {tasksJson[..Math.Min(100, tasksJson.Length)]}";
        }
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

    public void Dispose()
    {
        if (!_disposed)
        {
            (_llmService as IDisposable)?.Dispose();
            _disposed = true;
        }
    }
}
