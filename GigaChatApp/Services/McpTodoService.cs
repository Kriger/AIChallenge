using Microsoft.Extensions.Configuration;
using GigaChatApp.Infrastructure;
using System.Text.Json;
using System.Net.Http.Headers;

namespace GigaChatApp.Services;

/// <summary>
/// Сервис для работы с TodoMCP (Todoшница) через HTTP.
/// Работает напрямую с JSON-RPC, без MCP SDK.
/// </summary>
public sealed class McpTodoService : IAsyncDisposable
{
    private readonly Action<string, LogLevel> _log;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _http;
    private bool _connected;
    private string? _baseUrl;
    private List<TodoTool>? _tools;
    private int _nextId = 1;
    private string? _sessionId;

    public bool IsConnected => _connected;
    public IReadOnlyList<TodoTool> Tools => _tools ?? [];

    public McpTodoService(Action<string, LogLevel> log, IConfiguration configuration)
    {
        _log = log;
        _configuration = configuration;

        _http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
            BaseAddress = new Uri("https://placeholder"),
        };
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
    }

    public async Task ConnectAsync()
    {
        if (_connected)
            return;

        var mcpConfig = _configuration.GetSection("Mcp");
        var todoSection = mcpConfig.GetSection("Servers:Todo");
        if (!todoSection.Exists())
        {
            Log("Конфигурация TodoMCP не найдена", LogLevel.Warning);
            return;
        }

        _baseUrl = todoSection["BaseUrl"] ?? "https://localhost:7162";
        _http.BaseAddress = new Uri(_baseUrl);
        var username = todoSection["Username"];
        var password = todoSection["Password"];

        // Шаг 1: Логин
        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            Log($"🔐 Авторизация на {_baseUrl}...", LogLevel.Info);

            var authBody = new { username, password };
            var response = await _http.PostAsJsonAsync("/api/auth/login", authBody);
            if (response.IsSuccessStatusCode)
            {
                var rawAuth = await response.Content.ReadAsStringAsync();

                var authResult = JsonSerializer.Deserialize<JsonElement?>(rawAuth);
                if (authResult is not null)
                {
                    string? token = null;
                    if (authResult.Value.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String)
                        token = t.GetString();
                    if (string.IsNullOrEmpty(token) && authResult.Value.TryGetProperty("accessToken", out var a) && a.ValueKind == JsonValueKind.String)
                        token = a.GetString();
                    if (string.IsNullOrEmpty(token) && authResult.Value.TryGetProperty("access_token", out var at) && at.ValueKind == JsonValueKind.String)
                        token = at.GetString();

                    if (!string.IsNullOrEmpty(token))
                    {
                        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        Log($"✅ Авторизация успешна", LogLevel.Info);
                    }
                    else
                    {
                        Log("❌ Токен не найден в ответе авторизации", LogLevel.Warning);
                    }
                }
            }
            else
            {
                var err = await response.Content.ReadAsStringAsync();
                Log($"❌ Ошибка авторизации: {response.StatusCode} — {err}", LogLevel.Error);
            }
        }

        // Шаг 2: MCP initialize
        Log($"⏳ Подключение к MCP...", LogLevel.Info);
        var initPayload = new
        {
            jsonrpc = "2.0",
            id = _nextId++,
            @method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "GigaChatApp", version = "1.0.0" }
            }
        };

        var initResponse = await _http.PostAsJsonAsync("/mcp", initPayload);
        if (!initResponse.IsSuccessStatusCode)
        {
            var err = await initResponse.Content.ReadAsStringAsync();
            Log($"❌ Ошибка initialize: {initResponse.StatusCode} — {err}", LogLevel.Error);
            throw new Exception($"Initialize failed: {initResponse.StatusCode}");
        }

        var initRaw = await initResponse.Content.ReadAsStringAsync();
        var initJson = ExtractJson(initRaw);
        var initResult = JsonSerializer.Deserialize<JsonElement?>(initJson);
        if (initResult is null)
            throw new Exception("Пустой ответ initialize");

        // Проверяем, есть ли SSE-эндпоинт в ответе
        var resultObj = initResult.Value;
        if (resultObj.TryGetProperty("result", out var resultProp) && resultProp.ValueKind == JsonValueKind.Object)
        {
            if (resultProp.TryGetProperty("sseUrl", out var sseUrl) && sseUrl.ValueKind == JsonValueKind.String)
            {
                Log($"📡 SSE URL: {sseUrl.GetString()}", LogLevel.Info);
            }
            if (resultProp.TryGetProperty("messageId", out var msgId) && msgId.ValueKind == JsonValueKind.String)
            {
                _sessionId = msgId.GetString();
                _http.DefaultRequestHeaders.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
                Log($"📡 Session: {_sessionId}", LogLevel.Info);
            }
        }

        // Шаг 3: пропускаем notifications/initialized — сервер работает без инициализации
        // Пользователь отправляет tools/call напрямую, без init-flow

        // Шаг 4: list tools
        Log($"📦 Запрос инструментов...", LogLevel.Info);
        var toolsPayload = new
        {
            jsonrpc = "2.0",
            id = _nextId++,
            @method = "tools/list"
        };

        var toolsResponse = await _http.PostAsJsonAsync("/mcp", toolsPayload);
        if (!toolsResponse.IsSuccessStatusCode)
        {
            var err = await toolsResponse.Content.ReadAsStringAsync();
            Log($"❌ Ошибка tools/list: {toolsResponse.StatusCode} — {err}", LogLevel.Error);
            throw new Exception($"tools/list failed: {toolsResponse.StatusCode}");
        }

        var toolsRaw = await toolsResponse.Content.ReadAsStringAsync();
        var toolsJson = ExtractJson(toolsRaw);
        var toolsResult = JsonSerializer.Deserialize<JsonElement?>(toolsJson);
        if (toolsResult is null)
            throw new Exception("Пустой ответ tools/list");

        var toolsProp = toolsResult.Value.GetProperty("result");
        if (toolsProp.TryGetProperty("tools", out var toolsArray))
        {
            _tools = toolsArray.EnumerateArray()
                .Select(t => new TodoTool
                {
                    Name = t.GetProperty("name").GetString() ?? "unknown",
                    Description = t.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    InputSchema = t.TryGetProperty("inputSchema", out var s) ? s.GetRawText() : "{}"
                })
                .ToList();
        }

        _connected = true;
        Log($"✅ TodoMCP подключён. Инструментов: {_tools?.Count ?? 0}", LogLevel.Info);
    }

    public async Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        if (!_connected)
            throw new InvalidOperationException("MCP не подключён");

        // Не нормализуем аргументы здесь — это делает McpToolRegistry.ExecuteToolCall
        var args = arguments ?? new Dictionary<string, object?>();

        // Нормализуем аргументы для update_todo_item — переводим русские значения в английские
        if (toolName.Equals("update_todo_item", StringComparison.OrdinalIgnoreCase) && args is not null)
        {
            var argsNormalized = args.Keys.ToDictionary(k => k.ToLowerInvariant(), v => args[v]);

            // priority: русский → английский
            if (argsNormalized.TryGetValue("priority", out var rawPriority))
            {
                var priorityStr = NormalizeToString(rawPriority);
                if (!string.IsNullOrEmpty(priorityStr))
                {
                    var normalizedPriority = priorityStr.ToLowerInvariant() switch
                    {
                        "низкий" or "low" => "Low",
                        "базовый" or "basic" => "Basic",
                        "высокий" or "high" => "High",
                        "очень высокий" or "оченьвысокий" or "veryhigh" or "very high" => "VeryHigh",
                        "критический" or "критичный" or "critical" or "urgent" => "Critical",
                        _ => priorityStr
                    };
                    args = new Dictionary<string, object?>(args)
                    {
                        ["priority"] = normalizedPriority
                    };
                }
            }

            // isCompleted: русский → boolean
            if (argsNormalized.TryGetValue("iscompleted", out var rawCompleted))
            {
                var completedStr = NormalizeToString(rawCompleted);
                if (completedStr != null)
                {
                    bool newIsCompleted = completedStr.ToLowerInvariant() switch
                    {
                        "true" or "1" or "да" or "выполнена" or "выполнено" or "yes" or "completed" => true,
                        "false" or "0" or "нет" or "невыполнена" or "невыполнено" or "no" or "incomplete" => false,
                        _ => false
                    };
                    args = new Dictionary<string, object?>(args)
                    {
                        ["isCompleted"] = newIsCompleted
                    };
                }
            }
        }

        // Для get_project: если передано название проекта но не передан id — разрешаем id по списку проектов
        if (toolName.Equals("get_project", StringComparison.OrdinalIgnoreCase) && args is not null)
        {
            var argsNormalized = args.Keys.ToDictionary(k => k.ToLowerInvariant(), v => args[v]);
            bool hasId = argsNormalized.ContainsKey("id");
            bool hasTitle = argsNormalized.ContainsKey("title") || argsNormalized.ContainsKey("название") || argsNormalized.ContainsKey("title_ru");

            if (!hasId && hasTitle)
            {
                string? titleValue = null;
                if (argsNormalized.TryGetValue("title", out var t1)) titleValue = NormalizeToString(t1);
                else if (argsNormalized.TryGetValue("название", out var t2)) titleValue = NormalizeToString(t2);
                else if (argsNormalized.TryGetValue("title_ru", out var t3)) titleValue = NormalizeToString(t3);

                if (!string.IsNullOrEmpty(titleValue))
                {
                    Log($"🔍 get_project: id не указан, ищем проект по названию '{titleValue}'", LogLevel.Info);
                    var listResult = await CallToolAsync("list_projects", null);
                    try
                    {
                        using var doc = JsonDocument.Parse(listResult);
                        var root = doc.RootElement;
                        if (root.ValueKind == JsonValueKind.Array)
                        {
                            int? foundId = null;
                            foreach (var item in root.EnumerateArray())
                            {
                                var itemTitle = TryGetPropertyAsString(item, "Title", "title", "Название", "name", "Subject", "subject");
                                if (!string.IsNullOrEmpty(itemTitle) && itemTitle.Equals(titleValue, StringComparison.OrdinalIgnoreCase))
                                {
                                    var idStr = TryGetPropertyAsString(item, "Id", "id", "ID", "ProjectId", "projectid");
                                    if (int.TryParse(idStr, out var parsedId))
                                    {
                                        foundId = parsedId;
                                        break;
                                    }
                                }
                            }
                            if (foundId.HasValue)
                            {
                                Log($"✅ Найден проект: ID={foundId.Value}", LogLevel.Info);
                                args = new Dictionary<string, object?>(args)
                                {
                                    ["id"] = foundId.Value
                                };
                            }
                            else
                            {
                                Log($"❌ Проект с названием '{titleValue}' не найден", LogLevel.Warning);
                                return $"Проект с названием '{titleValue}' не найден. Используйте /todo call list_projects для просмотра всех проектов.";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ Не удалось разрешить ID проекта по названию: {ex.Message}", LogLevel.Warning);
                    }
                }
            }
        }

        // Для get_todo_item: если передан title но не передан id — разрешаем id по списку задач
        if (toolName.Equals("get_todo_item", StringComparison.OrdinalIgnoreCase) && args is not null)
        {
            var argsNormalized = args.Keys.ToDictionary(k => k.ToLowerInvariant(), v => args[v]);
            bool hasId = argsNormalized.ContainsKey("id") || argsNormalized.ContainsKey("taskid") || argsNormalized.ContainsKey("задачаid");
            bool hasTitle = argsNormalized.ContainsKey("title") || argsNormalized.ContainsKey("название") || argsNormalized.ContainsKey("title_ru");

            if (!hasId && hasTitle)
            {
                string? titleValue = null;
                if (argsNormalized.TryGetValue("title", out var t1)) titleValue = NormalizeToString(t1);
                else if (argsNormalized.TryGetValue("название", out var t2)) titleValue = NormalizeToString(t2);
                else if (argsNormalized.TryGetValue("title_ru", out var t3)) titleValue = NormalizeToString(t3);

                if (!string.IsNullOrEmpty(titleValue))
                {
                    Log($"🔍 get_todo_item: id не указан, ищем задачу по названию '{titleValue}'", LogLevel.Info);
                    var listResult = await CallToolAsync("list_todo_items", null);
                    try
                    {
                        using var doc = JsonDocument.Parse(listResult);
                        var root = doc.RootElement;
                        if (root.ValueKind == JsonValueKind.Array)
                        {
                            int? foundId = null;
                            foreach (var item in root.EnumerateArray())
                            {
                                var itemTitle = TryGetPropertyAsString(item, "Title", "title", "Название", "name", "Subject", "subject");
                                if (!string.IsNullOrEmpty(itemTitle) && itemTitle.Equals(titleValue, StringComparison.OrdinalIgnoreCase))
                                {
                                    var idStr = TryGetPropertyAsString(item, "Id", "id", "ID", "TaskId", "taskid");
                                    if (int.TryParse(idStr, out var parsedId))
                                    {
                                        foundId = parsedId;
                                        break;
                                    }
                                }
                            }
                            if (foundId.HasValue)
                            {
                                Log($"✅ Найдена задача: ID={foundId.Value}", LogLevel.Info);
                                args = new Dictionary<string, object?>(args)
                                {
                                    ["id"] = foundId.Value
                                };
                            }
                            else
                            {
                                Log($"❌ Задача с названием '{titleValue}' не найдена", LogLevel.Warning);
                                return $"Задача с названием '{titleValue}' не найдена. Используйте /todo list для просмотра всех задач.";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ Не удалось разрешить ID по названию: {ex.Message}", LogLevel.Warning);
                    }
                }
            }
        }
        
        var payload = new
        {
            jsonrpc = "2.0",
            id = _nextId++,
            @method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = args
            }
        };

        var payloadRaw = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        // Валидируем payload перед отправкой
        try
        {
            JsonDocument.Parse(payloadRaw);
        }
        catch (Exception ex)
        {
            Log($"❌ Невалидный JSON-RPC payload: {ex.Message}", LogLevel.Error);
            Log($"   Payload: {payloadRaw}", LogLevel.Error);
            throw new Exception($"Invalid payload: {ex.Message}");
        }

        var response = await _http.PostAsync("/mcp", new StringContent(payloadRaw, System.Text.Encoding.UTF8, "application/json"));
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            Log($"❌ Ошибка {toolName}: {response.StatusCode} — {err}", LogLevel.Error);
            throw new Exception($"Call failed: {response.StatusCode}");
        }

        var responseRaw = await response.Content.ReadAsStringAsync();
        
        var json = ExtractJson(responseRaw);
        var result = JsonSerializer.Deserialize<JsonElement?>(json);
        if (result is null)
            return "(пустой результат)";

        var resProp = result.Value.GetProperty("result");
        if (resProp.TryGetProperty("isError", out var isError) && isError.GetBoolean())
        {
            var errMsg = resProp.TryGetProperty("content", out var contentArr)
                ? contentArr.EnumerateArray()
                    .Where(c => c.TryGetProperty("type", out var t) && t.GetString() == "text")
                    .Select(c => c.GetProperty("text").GetString() ?? "")
                    .FirstOrDefault() ?? "Unknown error"
                : "Unknown error";
            throw new Exception(errMsg);
        }

        if (resProp.TryGetProperty("content", out var contentArr2))
        {
            var texts = contentArr2.EnumerateArray()
                .Where(c => c.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(c => c.GetProperty("text").GetString() ?? "")
                .ToList();

            var rawText = string.Join("\n", texts);

            // Клиентская фильтрация по проекту — сервер игнорирует projectTitle
            if (toolName.Equals("list_todo_items", StringComparison.OrdinalIgnoreCase) && arguments is not null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawText);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        // Нормализуем ключи аргументов к нижнему регистру
                        var argsNormalized = arguments.Keys.ToDictionary(
                            k => k.ToLowerInvariant(), 
                            v => arguments[v]);

                        string? filterProject = null;
                        if (argsNormalized.TryGetValue("projecttitle", out var pt) || 
                            argsNormalized.TryGetValue("project", out pt) ||
                            argsNormalized.TryGetValue("названиепроекта", out pt) ||
                            argsNormalized.TryGetValue("проект", out pt))
                        {
                            filterProject = NormalizeToString(pt);
                        }
                        else if (argsNormalized.TryGetValue("filter_by_project", out var fb))
                        {
                            var fbStr = NormalizeToString(fb);
                            if (fbStr != null)
                            {
                                if (fbStr.Equals("false", StringComparison.OrdinalIgnoreCase) || fbStr.Equals("нет", StringComparison.OrdinalIgnoreCase))
                                    filterProject = ""; // false → задачи без проекта
                                else
                                    filterProject = fbStr;
                            }
                        }

                        if (!string.IsNullOrEmpty(filterProject))
                        {
                            var filtered = new List<JsonElement>();
                            foreach (var item in root.EnumerateArray())
                            {
                                var projId = TryGetPropertyAsString(item, "ProjectTitle", "projectTitle", "Project", "project", "Проект", "проект", "НазваниеПроекта", "name");
                                if (string.Equals(projId, filterProject, StringComparison.OrdinalIgnoreCase))
                                {
                                    filtered.Add(item.Clone());
                                }
                            }
                            if (filtered.Count > 0)
                            {
                                var options = new JsonSerializerOptions 
                                { 
                                    WriteIndented = true,
                                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                                };
                                rawText = JsonSerializer.Serialize(filtered, options);
                            }
                        }
                        else if (filterProject != null)
                        {
                            // filterProject is empty — show tasks WITHOUT a project
                            var filtered = new List<JsonElement>();
                            foreach (var item in root.EnumerateArray())
                            {
                                var projId = TryGetPropertyAsString(item, "ProjectTitle", "projectTitle", "Project", "project", "Проект", "проект", "НазваниеПроекта", "name");
                                if (string.IsNullOrEmpty(projId) || 
                                    projId.Equals("нет", StringComparison.OrdinalIgnoreCase) ||
                                    projId.Equals("none", StringComparison.OrdinalIgnoreCase))
                                {
                                    filtered.Add(item.Clone());
                                }
                            }
                            if (filtered.Count > 0)
                            {
                                var options = new JsonSerializerOptions 
                                { 
                                    WriteIndented = true,
                                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                                };
                                rawText = JsonSerializer.Serialize(filtered, options);
                            }
                        }
                    }
                }
                catch { /* не фильтруем */ }
            }
            
            // Клиентская фильтрация по isCompleted — сервер может игнорировать
            if (toolName.Equals("list_todo_items", StringComparison.OrdinalIgnoreCase) && arguments is not null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawText);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        // Нормализуем ключи аргументов к нижнему регистру
                        var argsNormalized = arguments.Keys.ToDictionary(
                            k => k.ToLowerInvariant(), 
                            v => arguments[v]);

                        bool filterByCompleted = false;
                        bool? shouldFilter = null;
                        
                        if (argsNormalized.TryGetValue("iscompleted", out var ic))
                        {
                            var icStr = NormalizeToString(ic);
                            if (icStr != null)
                            {
                                filterByCompleted = icStr.Equals("true", StringComparison.OrdinalIgnoreCase) || 
                                                    icStr.Equals("да", StringComparison.OrdinalIgnoreCase);
                                shouldFilter = true;
                            }
                        }
                        else if (argsNormalized.TryGetValue("description", out var desc))
                        {
                            var descStr = NormalizeToString(desc);
                            if (descStr != null)
                            {
                                var descText = descStr.ToLowerInvariant();
                                if (descText.Contains("выполнен"))
                                {
                                    filterByCompleted = true;
                                    shouldFilter = true;
                                }
                                else if (descText.Contains("невыполнен"))
                                {
                                    filterByCompleted = false;
                                    shouldFilter = true;
                                }
                            }
                        }
                        
                        if (shouldFilter == true)
                        {
                            var filtered = new List<JsonElement>();
                            foreach (var item in root.EnumerateArray())
                            {
                                var isCompleted = TryGetPropertyAsString(item, "IsCompleted", "isCompleted", "СтатусВыполнения", "статусВыполнения", "Выполнена", "выполнена", "Status", "status");
                                bool itemCompleted = isCompleted != null && 
                                    (isCompleted.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                     isCompleted.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                                     isCompleted.Equals("выполнена", StringComparison.OrdinalIgnoreCase) ||
                                     isCompleted.Equals("выполнено", StringComparison.OrdinalIgnoreCase) ||
                                     isCompleted.Equals("completed", StringComparison.OrdinalIgnoreCase));
                                
                                if (itemCompleted == filterByCompleted)
                                {
                                    filtered.Add(item.Clone());
                                }
                            }
                            if (filtered.Count > 0)
                            {
                                var options = new JsonSerializerOptions 
                                { 
                                    WriteIndented = true,
                                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                                };
                                rawText = JsonSerializer.Serialize(filtered, options);
                            }
                        }
                    }
                }
                catch { /* не фильтруем */ }
            }
            
            // Форматируем JSON-ответы для читаемости
            var formatted = FormatResponse(rawText);
            return formatted;
        }

        return result.Value.GetRawText();
    }

    /// <summary>
    /// Форматирует JSON-ответ: Unicode → UTF-8, pretty-print, читаемые поля.
    /// Обработка вложенных JSON-строк (MCP возвращает text как JSON-encoded string).
    /// </summary>
    private static string FormatResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        raw = raw.Trim();

        // Если это массив или объект — форматируем как JSON с отступами
        if ((raw.StartsWith("[") || raw.StartsWith("{")) && !raw.StartsWith("\""))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                // Форматируем с отступами для читаемости
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                };
                return doc.RootElement.GetRawText();
            }
            catch
            {
                return System.Text.RegularExpressions.Regex.Unescape(raw);
            }
        }

        // MCP возвращает text как JSON-encoded string: "{\"Id\": 5, ...}"
        // Нужно распаковать строку, затем попытаться отформатировать содержимое как JSON
        if (raw.StartsWith("\"") && raw.Length >= 2)
        {
            try
            {
                // Распаковываем JSON-строку
                var unescaped = System.Text.RegularExpressions.Regex.Unescape(raw);
                
                // Проверяем, является ли распакованное содержимое JSON-объектом/массивом
                var trimmed = unescaped.Trim();
                if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(trimmed);
                        return doc.RootElement.GetRawText();
                    }
                    catch
                    {
                        return unescaped;
                    }
                }
                
                return unescaped;
            }
            catch
            {
                return raw;
            }
        }

        return raw;
    }

    public string GetToolsDescription()
    {
        if (_tools is null)
            return "TodoMCP не подключён.";

        var sb = new StringBuilder();
        sb.AppendLine($"📋 Инструменты TodoMCP ({_tools.Count}):");
        foreach (var tool in _tools)
        {
            sb.AppendLine($"  • **{tool.Name}**");
            sb.AppendLine($"    {tool.Description}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private void Log(string message, LogLevel level)
    {
        _log?.Invoke(message, level);
    }

    /// <summary>
    /// Приводит значение аргумента к строке, обрабатывая string/int/bool/JsonElement.
    /// </summary>
    private static string? NormalizeToString(object? value)
    {
        if (value == null)
            return null;

        return value switch
        {
            string s => s,
            JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString(),
            bool b => b.ToString().ToLowerInvariant(),
            int or long or float or double or decimal => value.ToString(),
            _ => value.ToString()
        };
    }

    /// <summary>
    /// Извлекает строковое значение свойства независимо от регистра.
    /// </summary>
    private static string? TryGetPropertyAsString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                var val = prop.ValueKind switch
                {
                    JsonValueKind.String => prop.GetString(),
                    JsonValueKind.Number => prop.GetInt32().ToString(),
                    JsonValueKind.True or JsonValueKind.False => prop.GetBoolean().ToString().ToLowerInvariant(),
                    JsonValueKind.Null => null,
                    _ => prop.ToString()
                };
                return val;
            }
        }
        return null;
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Извлекает JSON из SSE-формата (event: message\ndata: {...}).
    /// </summary>
    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new Exception("Пустой ответ");

        // Если уже чистый JSON — возвращаем как есть
        if (raw.TrimStart().StartsWith("{") || raw.TrimStart().StartsWith("["))
            return raw.Trim();

        // Иначе ищем JSON внутри SSE-формата
        var dataIdx = raw.IndexOf("data:");
        if (dataIdx >= 0)
        {
            var json = raw[(dataIdx + 5)..].Trim();
            // Иногда data идёт несколько раз, берём последний
            var lastDataIdx = raw.LastIndexOf("data:");
            if (lastDataIdx > dataIdx)
                json = raw[(lastDataIdx + 5)..].Trim();
            return json;
        }

        // Fallback: ищем { в тексте
        var braceIdx = raw.IndexOf('{');
        if (braceIdx >= 0)
            return raw[braceIdx..].Trim();

        throw new Exception($"Не удалось извлечь JSON из: {raw[..Math.Min(100, raw.Length)]}");
    }
}

public record TodoTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string InputSchema { get; init; }
}
