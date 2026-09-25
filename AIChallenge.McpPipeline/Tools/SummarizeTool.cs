using AIChallenge.McpScheduler;
using System.Text;
using System.Text.Json;

namespace AIChallenge.McpPipeline.Tools;

/// <summary>
/// Инструмент summarize — обработка данных и генерация отчёта.
/// </summary>
public sealed class SummarizeTool : IPipelineTool, IDisposable
{
    private readonly ScheduledSummaryService? _summaryService;
    private readonly LlmSummaryService? _llmService;
    private readonly Action<string> _log;
    private bool _disposed;

    public string Summary { get; private set; } = "";
    public string Name => "summarize";
    public string Description => "Обработка задач и генерация отчёта. Режимы: stats, diff, llm.";

    public SummarizeTool(string? baseDirectory = null, Action<string>? log = null,
        LlmSummaryService? llmService = null)
    {
        var baseDir = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
        _log = log ?? (msg => Console.WriteLine($"  [summarize] {msg}"));
        _llmService = llmService;

        try
        {
            _summaryService = new ScheduledSummaryService(
                baseDir,
                msg => _log(msg),
                () => Task.FromResult("[]")
            );
        }
        catch (Exception ex)
        {
            _log($"⚠️ Не удалось создать ScheduledSummaryService: {ex.Message}");
        }
    }

    public async Task<string> ExecuteAsync(Dictionary<string, object?> parameters)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SummarizeTool));

        if (!parameters.TryGetValue("tasksJson", out var tasksJsonObj) || tasksJsonObj == null)
            return "{\"error\": \"Отсутствует параметр tasksJson\"}";

        var tasksJson = tasksJsonObj.ToString() ?? "[]";
        var mode = parameters.TryGetValue("mode", out var modeObj)
            ? (modeObj?.ToString()?.ToLowerInvariant() ?? "stats")
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
            _ => SummarizeStatsAsync(tasksJson)
        };
    }

    private async Task<string> SummarizeWithLlmAsync(string tasksJson, string? previousSummary)
    {
        try
        {
            var result = previousSummary != null
                ? await _llmService!.GenerateDiffSummaryAsync(tasksJson, previousSummary)
                : await _llmService!.GenerateSummaryAsync(tasksJson);

            _log("✅ LLM-отчёт сформирован");
            Summary = result;
            return result;
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка LLM: {ex.Message}");
            return $"{{\"error\": \"Ошибка LLM: {ex.Message}\"}}";
        }
    }

    private async Task<string> SummarizeWithLlmFallbackAsync(string tasksJson)
    {
        try
        {
            if (_summaryService != null)
            {
                var result = await _summaryService.TakeSnapshotAsync();
                _log("✅ Отчёт через ScheduledSummaryService");
                Summary = result;
                return result;
            }
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка fallback: {ex.Message}");
        }
        return GenerateBasicSummary(tasksJson);
    }

    private async Task<string> SummarizeDiffAsync(string tasksJson)
    {
        try
        {
            if (_summaryService != null)
            {
                var result = await _summaryService.TakeSnapshotAsync();
                _log("✅ Diff-отчёт");
                Summary = result;
                return result;
            }
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка diff: {ex.Message}");
        }
        return GenerateBasicSummary(tasksJson);
    }

    private string SummarizeStatsAsync(string tasksJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(tasksJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return "{\"error\": \"Ожидается JSON-массив\"}";

            var totalCount = 0;
            var completedCount = 0;
            var byProject = new Dictionary<string, int>();
            var byPriority = new Dictionary<string, int>();
            var completedTitles = new List<string>();
            var pendingTitles = new List<string>();

            foreach (var task in doc.RootElement.EnumerateArray())
            {
                totalCount++;

                var title = JsonUtils.ExtractString(task, "Title", "title", "Название", "name", "Subject");
                var isCompleted = JsonUtils.ExtractString(task, "IsCompleted", "isCompleted", "Status", "status", "Статус");
                var project = JsonUtils.ExtractString(task, "ProjectTitle", "projectTitle", "Project", "project");
                var rawPriority = JsonUtils.ExtractString(task, "Priority", "priority");
                var priority = MapPriorityToLabel(rawPriority);

                var completed = !string.IsNullOrEmpty(isCompleted) &&
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
                else if (!string.IsNullOrEmpty(title))
                {
                    pendingTitles.Add(title);
                }

                var projKey = string.IsNullOrEmpty(project) ? "Без проекта" : project;
                if (!byProject.TryGetValue(projKey, out var pc)) byProject[projKey] = 0;
                byProject[projKey]++;

                var priKey = string.IsNullOrEmpty(priority) ? "Без приоритета" : priority;
                if (!byPriority.TryGetValue(priKey, out var ppc)) byPriority[priKey] = 0;
                byPriority[priKey]++;
            }

            var sb = new StringBuilder();
            sb.AppendLine("📊 Сводка задач:");
            sb.AppendLine($"   Всего: {totalCount}");
            sb.AppendLine($"   ✅ Выполнено: {completedCount} ({GetPercent(completedCount, totalCount)}%)");
            sb.AppendLine($"   ⏳ Ожидает: {totalCount - completedCount}");
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
            Summary = sb.ToString();
            return Summary;
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
            var count = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
            return $"📊 Всего задач: {count}\n   Статистика сгенерирована.";
        }
        catch
        {
            return $"📊 Не удалось обработать данные.\n   Raw: {tasksJson[..Math.Min(100, tasksJson.Length)]}";
        }
    }

    private static string MapPriorityToLabel(string? rawPriority)
    {
        if (string.IsNullOrEmpty(rawPriority))
            return "Без приоритета";
        if (!int.TryParse(rawPriority, out var num))
            return rawPriority;

        return num switch
        {
            1 => "Низкий", 2 => "Базовый", 3 => "Высокий",
            4 => "Очень высокий", 5 => "Критический",
            _ => rawPriority
        };
    }

    private static int GetPercent(int part, int total) => total == 0 ? 0 : (int)((double)part / total * 100);

    public void Dispose()
    {
        if (_disposed) return;
        (_llmService as IDisposable)?.Dispose();
        _disposed = true;
    }
}
