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

        var payload = new
        {
            jsonrpc = "2.0",
            id = _nextId++,
            @method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = arguments ?? new Dictionary<string, object?>()
            }
        };

        var payloadRaw = JsonSerializer.Serialize(payload);

        var response = await _http.PostAsync("/mcp", new StringContent(payloadRaw, System.Text.Encoding.UTF8, "application/json"));
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            Log($"❌ Ошибка {toolName}: {response.StatusCode} — {err}", LogLevel.Error);
            throw new Exception($"Call failed: {response.StatusCode}");
        }

        var raw = await response.Content.ReadAsStringAsync();
        var json = ExtractJson(raw);
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

            // Форматируем JSON-ответы для читаемости
            var formatted = FormatResponse(string.Join("\n", texts));
            return formatted;
        }

        return result.Value.GetRawText();
    }

    /// <summary>
    /// Форматирует JSON-ответ: Unicode → UTF-8, pretty-print, читаемые поля.
    /// </summary>
    private static string FormatResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        raw = raw.Trim();

        // Если это массив или объект — форматируем как JSON
        if (raw.StartsWith("[") || raw.StartsWith("{"))
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                // Сначала десериализуем в JsonDocument для красивого вывода
                using var doc = JsonDocument.Parse(raw);
                var formatted = doc.RootElement.GetRawText();

                // Если JSON уже содержит Unicode-экраны — декодируем
                var decoded = System.Text.RegularExpressions.Regex.Unescape(formatted);

                // Форматируем ключи в читаемом виде
                return decoded;
            }
            catch
            {
                // Fallback: просто декодируем Unicode
                return System.Text.RegularExpressions.Regex.Unescape(raw);
            }
        }

        // Если это plain text — возвращаем как есть
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
