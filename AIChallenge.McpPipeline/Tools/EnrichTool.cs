using AIChallenge.McpScheduler;
using AIChallenge.Services;
using System.Text.Json;

namespace AIChallenge.McpPipeline.Tools;

/// <summary>
/// Инструмент enrich — приоритизация по срокам и обогащение описаний.
/// </summary>
public sealed class EnrichTool : IPipelineTool, IDisposable
{
    private readonly LlmSummaryService? _llmService;
    private readonly McpTodoService? _mcpService;
    private readonly Action<string> _log;
    private bool _disposed;

    public string Summary { get; private set; } = "";
    public string Name => "enrich";
    public string Description => "Приоритизация задач по срокам и обогащение описаний. Режимы: prioritize, enrich, both.";

    public EnrichTool(LlmSummaryService? llmService = null, McpTodoService? mcpService = null, Action<string>? log = null)
    {
        _llmService = llmService;
        _mcpService = mcpService;
        _log = log ?? (msg => Console.WriteLine($"  [enrich] {msg}"));
    }

    public async Task<string> ExecuteAsync(Dictionary<string, object?> parameters)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EnrichTool));

        if (!parameters.TryGetValue("tasksJson", out var tasksJsonObj) || tasksJsonObj == null)
            return "{\"error\": \"Отсутствует параметр tasksJson\"}";

        var tasksJson = tasksJsonObj.ToString() ?? "[]";
        var mode = parameters.TryGetValue("mode", out var modeObj)
            ? (modeObj?.ToString()?.ToLowerInvariant() ?? "both")
            : "both";

        var applyChanges = parameters.TryGetValue("applyChanges", out var acObj)
            && acObj?.ToString()?.ToLowerInvariant() == "true";

        int deadlineDaysUrgent = 3;
        if (parameters.TryGetValue("deadlineDaysUrgent", out var du) && du != null)
            int.TryParse(du.ToString(), out deadlineDaysUrgent);

        int deadlineDaysHigh = 7;
        if (parameters.TryGetValue("deadlineDaysHigh", out var dh) && dh != null)
            int.TryParse(dh.ToString(), out deadlineDaysHigh);

        _log($"🔧 Обогащение в режиме: {mode}");

        try
        {
            using var doc = JsonDocument.Parse(tasksJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return "{\"error\": \"Ожидается JSON-массив\"}";

            var enrichedTasks = new List<JsonElement>();
            var prioritizedCount = 0;
            var colorChangedCount = 0;
            var enrichedCount = 0;

            foreach (var task in doc.RootElement.EnumerateArray())
            {
                var enriched = EnrichTask(task, mode, deadlineDaysUrgent, deadlineDaysHigh);
                enrichedTasks.Add(enriched);

                if (IsPrioritizeMode(mode))
                {
                    var oldPriority = task.GetProperty("Priority").GetInt32();
                    var newPriority = enriched.GetProperty("Priority").GetInt32();
                    if (oldPriority != newPriority)
                        prioritizedCount++;

                    var oldColor = task.TryGetProperty("Color", out var oc) && oc.ValueKind == JsonValueKind.String ? oc.GetString() : null;
                    var newColor = enriched.TryGetProperty("Color", out var nc) && nc.ValueKind == JsonValueKind.String ? nc.GetString() : null;
                    if (oldColor != newColor)
                        colorChangedCount++;
                }

                if (IsEnrichMode(mode))
                {
                    var oldDesc = task.TryGetProperty("Description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                    var newDesc = enriched.TryGetProperty("Description", out var nd) && nd.ValueKind == JsonValueKind.String ? nd.GetString() : null;
                    if (string.IsNullOrEmpty(oldDesc) && !string.IsNullOrEmpty(newDesc))
                        enrichedCount++;
                }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var result = JsonSerializer.Serialize(enrichedTasks.ToArray(), options);

            // Сохраняем изменения в MCP, если нужно
            if (applyChanges && _mcpService != null)
            {
                await ApplyChangesToMcpAsync(doc, enrichedTasks);
            }

            var updatedCount = prioritizedCount + enrichedCount;
            Summary = $"Обновлено задач: {updatedCount} (приоритеты: {prioritizedCount}, цвета: {colorChangedCount}, описаний: {enrichedCount})";
            _log($"✅ {Summary}");

            return result;
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка обогащения: {ex.Message}");
            Summary = $"Ошибка: {ex.Message}";
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }

    private static bool IsPrioritizeMode(string mode) => mode.Contains("prioritize") || mode == "both";
    private static bool IsEnrichMode(string mode) => mode.Contains("enrich") || mode == "both";

    private async Task ApplyChangesToMcpAsync(JsonDocument originalDoc, List<JsonElement> enrichedTasks)
    {
        int applied = 0;
        int errors = 0;

        var tasks = originalDoc.RootElement.EnumerateArray().ToList();
        for (int i = 0; i < tasks.Count && i < enrichedTasks.Count; i++)
        {
            var original = tasks[i];
            var enriched = enrichedTasks[i];

            // Проверяем, есть ли изменения
            var changed = false;

            var oldPriority = original.GetProperty("Priority").GetInt32();
            var newPriority = enriched.GetProperty("Priority").GetInt32();
            if (oldPriority != newPriority) changed = true;

            var oldColor = original.TryGetProperty("Color", out var oc) && oc.ValueKind == JsonValueKind.String ? oc.GetString() : null;
            var newColor = enriched.TryGetProperty("Color", out var nc) && nc.ValueKind == JsonValueKind.String ? nc.GetString() : null;
            if (oldColor != newColor) changed = true;

            var oldDesc = original.TryGetProperty("Description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            var newDesc = enriched.TryGetProperty("Description", out var nd) && nd.ValueKind == JsonValueKind.String ? nd.GetString() : null;
            if (oldDesc != newDesc) changed = true;

            if (!changed) continue;

            int taskId = original.GetProperty("Id").GetInt32();

            try
            {
                var args = new Dictionary<string, object?> { ["id"] = taskId };

                if (oldPriority != newPriority)
                    args["priority"] = MapPriorityToString(newPriority);

                if (oldColor != newColor)
                    args["color"] = newColor;

                if (oldDesc != newDesc && !string.IsNullOrEmpty(newDesc))
                    args["description"] = newDesc;

                await _mcpService!.CallToolAsync("update_todo_item", args);
                applied++;
            }
            catch (Exception ex)
            {
                errors++;
                _log($"   ⚠️ Ошибка обновления задачи {taskId}: {ex.Message}");
            }
        }

        if (applied > 0)
            _log($"   💾 Применено изменений в MCP: {applied}{(errors > 0 ? $", ошибок: {errors}" : "")}");
    }

    private JsonElement EnrichTask(JsonElement task, string mode, int deadlineDaysUrgent, int deadlineDaysHigh)
    {
        var obj = new Dictionary<string, JsonElement>();
        foreach (var prop in task.EnumerateObject())
            obj[prop.Name] = prop.Value.Clone();

        if (IsPrioritizeMode(mode))
        {
            obj = PrioritizeTask(obj, deadlineDaysUrgent, deadlineDaysHigh);
            obj = SyncColor(obj);
        }

        if (IsEnrichMode(mode))
            obj = EnrichDescription(obj);

        return JsonDocument.Parse(JsonUtils.ToJsonString(obj)).RootElement;
    }

    private Dictionary<string, JsonElement> PrioritizeTask(Dictionary<string, JsonElement> task, int deadlineDaysUrgent, int deadlineDaysHigh)
    {
        var deadlineStr = JsonUtils.ExtractString(task, "Deadline", "deadline");
        var plannedStr = JsonUtils.ExtractString(task, "PlannedCompletionTime", "plannedCompletionTime");

        if (IsCompleted(task))
            return task;

        var currentPriority = task.TryGetValue("Priority", out var pri) && pri.ValueKind == JsonValueKind.Number
            ? pri.GetInt32() : 0;

        var daysUntilDeadline = GetDaysUntilDeadline(deadlineStr, plannedStr);

        int newPriority;
        if (daysUntilDeadline.HasValue)
        {
            if (daysUntilDeadline.Value < 0)
                newPriority = 5;
            else if (daysUntilDeadline.Value <= deadlineDaysUrgent)
                newPriority = Math.Max(currentPriority, 4);
            else if (daysUntilDeadline.Value <= deadlineDaysHigh)
                newPriority = Math.Max(currentPriority, 3);
            else if (daysUntilDeadline.Value <= 30)
                newPriority = Math.Max(currentPriority, 2);
            else
                newPriority = currentPriority > 0 ? currentPriority : 1;
        }
        else
        {
            newPriority = currentPriority > 0 ? currentPriority : 2;
        }

        if (newPriority != currentPriority)
        {
            task["Priority"] = JsonDocument.Parse($"{newPriority}").RootElement;
            task["PriorityReason"] = JsonDocument.Parse($"\"{JsonUtils.EscapeJsonString(GetPriorityReason(daysUntilDeadline, newPriority))}\"").RootElement;
            task["Color"] = JsonDocument.Parse($"\"{JsonUtils.EscapeJsonString(GetColorForPriority(newPriority))}\"").RootElement;
        }

        return task;
    }

    private static Dictionary<string, JsonElement> SyncColor(Dictionary<string, JsonElement> task)
    {
        if (!task.TryGetValue("Priority", out var pri) || pri.ValueKind != JsonValueKind.Number)
            return task;

        var expectedColor = GetColorForPriority(pri.GetInt32());
        var currentColor = task.TryGetValue("Color", out var col) && col.ValueKind == JsonValueKind.String ? col.GetString() : null;

        if (currentColor != expectedColor)
            task["Color"] = JsonDocument.Parse($"\"{JsonUtils.EscapeJsonString(expectedColor)}\"").RootElement;

        return task;
    }

    private Dictionary<string, JsonElement> EnrichDescription(Dictionary<string, JsonElement> task)
    {
        var description = JsonUtils.ExtractString(task, "Description", "description");
        if (!string.IsNullOrEmpty(description))
            return task;

        var title = JsonUtils.ExtractString(task, "Title", "title", "Название") ?? "";
        var fallbackDesc = $"Задача: {title}";
        task["Description"] = JsonDocument.Parse($"\"{JsonUtils.EscapeJsonString(fallbackDesc)}\"").RootElement;

        if (_llmService != null && !string.IsNullOrEmpty(title))
        {
            try
            {
                var generated = GenerateDescription(title);
                if (!string.IsNullOrEmpty(generated) && generated != fallbackDesc)
                {
                    task["Description"] = JsonDocument.Parse($"\"{JsonUtils.EscapeJsonString(generated)}\"").RootElement;
                    _log($"   📝 LLM-описание: {title[..Math.Min(30, title.Length)]}");
                }
            }
            catch { }
        }

        return task;
    }

    private string? GenerateDescription(string title)
    {
        try
        {
            var words = title.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 2)
                return title;
            return $"{words[0]}: {string.Join(" ", words[1..])}";
        }
        catch
        {
            return null;
        }
    }

    private int? GetDaysUntilDeadline(string? deadlineStr, string? plannedStr)
    {
        var deadline = ParseDate(deadlineStr);
        var planned = ParseDate(plannedStr);

        DateTime? closest = deadline;
        if (planned.HasValue && (closest == null || planned.Value < closest.Value))
            closest = planned;

        if (closest.HasValue)
            return (int)Math.Ceiling((closest.Value - DateTime.UtcNow).TotalDays);

        return null;
    }

    private static DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;
        return DateTime.TryParse(dateStr, out var date) ? date : null;
    }

    private static bool IsCompleted(Dictionary<string, JsonElement> task)
    {
        if (task.TryGetValue("IsCompleted", out var ic) && ic.ValueKind == JsonValueKind.True)
            return true;

        if (task.TryGetValue("IsCompleted", out ic) && ic.ValueKind == JsonValueKind.String)
        {
            var s = ic.GetString();
            return !string.IsNullOrEmpty(s) &&
                (s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                 s.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                 s.Equals("выполнена", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static string GetPriorityReason(int? daysUntilDeadline, int priority)
    {
        return daysUntilDeadline switch
        {
            < 0 => "Просрочена",
            <= 1 => "Срочно (сегодня)",
            <= 3 => "Срочно (в течение 3 дней)",
            <= 7 => "Близкий дедлайн",
            <= 30 => "В этом месяце",
            _ => MapPriorityLabel(priority)
        };
    }

    private static string MapPriorityLabel(int priority) => priority switch
    {
        1 => "Низкий", 2 => "Базовый", 3 => "Высокий",
        4 => "Очень высокий", 5 => "Критический", _ => "Неизвестный"
    };

    private static string GetColorForPriority(int priority) => priority switch
    {
        1 => "#6b7280", 2 => "#3b82f6", 3 => "#f59e0b",
        4 => "#ef4444", 5 => "#dc2626", _ => "#6b7280"
    };

    private static string MapPriorityToString(int priority) => priority switch
    {
        1 => "low", 2 => "basic", 3 => "high",
        4 => "veryhigh", 5 => "critical", _ => "basic"
    };

    public void Dispose()
    {
        if (_disposed) return;
        (_llmService as IDisposable)?.Dispose();
        _disposed = true;
    }
}
