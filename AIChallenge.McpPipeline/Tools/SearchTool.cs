using AIChallenge.Services;
using Microsoft.Extensions.Configuration;

namespace AIChallenge.McpPipeline.Tools;

/// <summary>
/// Инструмент search — получение данных из MCP-сервера.
/// Подключается к TodoMCP и возвращает задачи в формате JSON.
/// </summary>
public sealed class SearchTool : IDisposable
{
    private readonly McpTodoService? _mcpService;
    private readonly Action<string> _log;
    private bool _disposed;

    public string Name => "search";
    public string Description => "Получение задач из MCP-сервера. Возвращает JSON-массив задач с фильтрацией.";

    public SearchTool(IConfiguration? configuration = null, Action<string>? log = null)
    {
        _log = log ?? (msg => Console.WriteLine($"  [search] {msg}"));

        if (configuration != null)
        {
            try
            {
                _mcpService = new McpTodoService(
                    (msg, level) => _log($"[{level}] {msg}"),
                    configuration
                );
            }
            catch (Exception ex)
            {
                _log($"⚠️ Не удалось создать McpTodoService: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Выполняет поиск задач.
    /// Параметры: project (string?), status (string?), limit (int?)
    /// Возвращает: JSON-массив задач.
    /// </summary>
    public async Task<string> ExecuteAsync(Dictionary<string, object?> parameters)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SearchTool));

        _log("🔍 Поиск задач в MCP...");

        if (_mcpService == null)
        {
            // Fallback: возвращаем тестовые данные
            return GetFallbackData();
        }

        try
        {
            // Подключаемся к MCP
            if (!_mcpService.IsConnected)
            {
                await _mcpService.ConnectAsync();
            }

            // Если подключение не удалось — используем fallback
            if (!_mcpService.IsConnected)
            {
                _log("⚠️ MCP не подключён, используем тестовые данные");
                return GetFallbackData();
            }

            // Формируем аргументы для MCP
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

            // Вызываем MCP
            var result = await _mcpService.CallToolAsync("list_todo_items", args);

            // Фильтруем по лимиту, если нужно
            if (args.ContainsKey("limit") && result.StartsWith("["))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(result);
                    var array = doc.RootElement;
                    if (array.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var count = array.GetArrayLength();
                        if (count > (int)args["limit"]!)
                        {
                            var options = new System.Text.Json.JsonSerializerOptions
                            {
                                WriteIndented = true,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            };
                            var sliced = new System.Text.Json.JsonElement[count];
                            var idx = 0;
                            foreach (var item in array.EnumerateArray())
                            {
                                if (idx >= (int)args["limit"]!) break;
                                sliced[idx++] = item.Clone();
                            }
                            var arr = System.Text.Json.JsonSerializer.Serialize(sliced[..idx], options);
                            _log($"   Отфильтровано до {idx} задач");
                            return arr;
                        }
                    }
                }
                catch
                {
                    // Не фильтруем при ошибке
                }
            }

            _log($"✅ Найдено задач");
            return result;
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка поиска: {ex.Message}");
            return $"{{\"error\": \"{ex.Message}\"}}";
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

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            var service = _mcpService;
            if (service != null)
            {
                var asyncDisp = service as IAsyncDisposable;
                if (asyncDisp != null)
                {
                    await asyncDisp.DisposeAsync();
                }
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            var service = _mcpService;
            if (service != null)
            {
                var asyncDisp = service as IAsyncDisposable;
                if (asyncDisp != null)
                {
                    // IAsyncDisposable не имеет синхронного Dispose,
                    // но мы можем вызвать DisposeAsync и подождать
                    try
                    {
                        asyncDisp.DisposeAsync().AsTask().Wait(1000);
                    }
                    catch
                    {
                        // Игнорируем таймауты при диспоузе
                    }
                }
            }
            _disposed = true;
        }
    }
}
