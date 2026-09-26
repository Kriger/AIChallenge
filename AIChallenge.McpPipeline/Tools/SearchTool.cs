using AIChallenge.Models;
using AIChallenge.Services;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace AIChallenge.McpPipeline.Tools;

/// <summary>
/// Инструмент search — получение данных из MCP-сервера.
/// </summary>
public sealed class SearchTool : IPipelineTool, IDisposable
{
    private readonly McpTodoService? _mcpService;
    private readonly Action<string> _log;
    private bool _disposed;

    public string Summary { get; private set; } = "";
    public string Name => "search";
    public string Description => "Получение задач из MCP-сервера. Возвращает JSON-массив задач с фильтрацией.";

    public SearchTool(McpTodoService? mcpService = null, Action<string>? log = null)
    {
        _mcpService = mcpService;
        _log = log ?? (msg => Console.WriteLine($"  [search] {msg}"));
    }

    public async Task<string> ExecuteAsync(Dictionary<string, object?> parameters)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SearchTool));

        _log("🔍 Поиск задач в MCP...");

        if (_mcpService == null)
        {
            var fallback = GetFallbackData();
            Summary = "Тестовые данные (MCP не подключён)";
            return fallback;
        }

        try
        {
            if (!_mcpService.IsConnected)
                await _mcpService.ConnectAsync();

            if (!_mcpService.IsConnected)
            {
                _log("⚠️ MCP не подключён, используем тестовые данные");
                Summary = "Тестовые данные (MCP не подключён)";
                return GetFallbackData();
            }

            var args = BuildArgs(parameters);
            var result = await _mcpService.CallToolAsync("list_todo_items", args);

            if (args.TryGetValue("limit", out var limitVal) && result.StartsWith("["))
            {
                result = ApplyLimit(result, (int)limitVal!);
            }

            var stats = ExtractStats(result);
            Summary = $"Найдено задач: {stats.TaskCount}, проектов: {stats.ProjectCount}";
            _log($"✅ {Summary}");

            return JsonUtils.FormatJson(result);
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка поиска: {ex.Message}");
            Summary = $"Ошибка: {ex.Message}";
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }

    private Dictionary<string, object?> BuildArgs(Dictionary<string, object?> parameters)
    {
        var args = new Dictionary<string, object?>();

        if (parameters.TryGetValue("project", out var project) && project != null)
        {
            args["projectTitle"] = project.ToString();
            _log($"   Фильтр по проекту: {project}");
        }

        if (parameters.TryGetValue("status", out var status) && status != null)
        {
            var statusStr = status.ToString()?.ToLowerInvariant();
            if (statusStr == "completed" || statusStr == "выполн")
            {
                args["isCompleted"] = true;
                _log("   Фильтр: выполненные задачи");
            }
            else if (statusStr == "pending" || statusStr == "ожидает")
            {
                args["isCompleted"] = false;
                _log("   Фильтр: ожидающие задачи");
            }
        }

        if (parameters.TryGetValue("limit", out var limit) && limit != null)
        {
            if (int.TryParse(limit.ToString(), out var parsedLimit) && parsedLimit > 0)
            {
                args["limit"] = parsedLimit;
                _log($"   Лимит: {parsedLimit}");
            }
        }

        return args;
    }

    private string ApplyLimit(string json, int limit)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var array = doc.RootElement;
            if (array.ValueKind != JsonValueKind.Array)
                return json;

            var count = array.GetArrayLength();
            if (count <= limit)
                return json;

            var elements = array.EnumerateArray().Take(limit).Select(e => e.Clone()).ToList();
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            _log($"   Отфильтровано до {limit} задач");
            return JsonSerializer.Serialize(elements.ToArray(), options);
        }
        catch
        {
            return json;
        }
    }

    private (int TaskCount, int ProjectCount) ExtractStats(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var taskCount = doc.RootElement.GetArrayLength();
            var projects = new HashSet<string>();

            foreach (var task in doc.RootElement.EnumerateArray())
            {
                var proj = JsonUtils.ExtractString(task, "ProjectTitle", "projectTitle", "Project", "project");
                if (!string.IsNullOrEmpty(proj))
                    projects.Add(proj);
            }

            return (taskCount, projects.Count);
        }
        catch
        {
            return (0, 0);
        }
    }

    private string GetFallbackData()
    {
        _log("ℹ️  Используем тестовые данные (MCP не подключён)");
        return """
            [
              {"Id": 1, "Title": "Изучить MCP протокол", "Status": "completed", "ProjectTitle": "Обучение", "Priority": "High"},
              {"Id": 2, "Title": "Написать пайплайн", "Status": "pending", "ProjectTitle": "Проект", "Priority": "Critical"},
              {"Id": 3, "Title": "Протестировать инструменты", "Status": "pending", "ProjectTitle": "Проект", "Priority": "Basic"}
            ]
            """;
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_mcpService is IAsyncDisposable asyncDisp)
        {
            try { asyncDisp.DisposeAsync().AsTask().Wait(1000); } catch { }
        }
        _disposed = true;
    }
}
