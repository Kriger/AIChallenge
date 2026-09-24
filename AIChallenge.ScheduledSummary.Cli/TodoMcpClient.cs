using System.Net.Http.Headers;
using System.Text.Json;

namespace AIChallenge.ScheduledSummary.Cli;

/// <summary>
/// Лёгкий клиент для подключения к TodoMCP.
/// </summary>
public sealed class TodoMcpClient : IDisposable, IAsyncDisposable
{
    private readonly HttpClient _http;
    private bool _connected;
    private string? _baseUrl;
    private int _nextId = 1;
    private string? _sessionId;

    public bool IsConnected => _connected;

    public TodoMcpClient(string baseUrl, string? username = null, string? password = null)
    {
        _baseUrl = baseUrl;
        _http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
            BaseAddress = new Uri(_baseUrl),
        };
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            var authBody = new { username, password };
            var response = _http.PostAsJsonAsync("/api/auth/login", authBody).Result;
            if (response.IsSuccessStatusCode)
            {
                var rawAuth = response.Content.ReadAsStringAsync().Result;
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
                    }
                }
            }
        }
    }

    public async Task ConnectAsync()
    {
        if (_connected)
            return;

        var initPayload = new
        {
            jsonrpc = "2.0",
            id = _nextId++,
            @method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "ScheduledSummaryCli", version = "1.0.0" }
            }
        };

        var initResponse = await _http.PostAsJsonAsync("/mcp", initPayload);
        if (!initResponse.IsSuccessStatusCode)
        {
            var err = await initResponse.Content.ReadAsStringAsync();
            throw new Exception($"Initialize failed: {initResponse.StatusCode} — {err}");
        }

        var initRaw = await initResponse.Content.ReadAsStringAsync();
        var initJson = ExtractJson(initRaw);
        var initResult = JsonSerializer.Deserialize<JsonElement?>(initJson);
        if (initResult is null)
            throw new Exception("Пустой ответ initialize");

        var resultObj = initResult.Value;
        if (resultObj.TryGetProperty("result", out var resultProp) && resultProp.ValueKind == JsonValueKind.Object)
        {
            if (resultProp.TryGetProperty("messageId", out var msgId) && msgId.ValueKind == JsonValueKind.String)
            {
                _sessionId = msgId.GetString();
                _http.DefaultRequestHeaders.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
            }
        }

        _connected = true;
    }

    public async Task<string> ListTodoItemsAsync()
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
                name = "list_todo_items",
                arguments = new Dictionary<string, object?>()
            }
        };

        var payloadRaw = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var response = await _http.PostAsync("/mcp", new StringContent(payloadRaw, System.Text.Encoding.UTF8, "application/json"));
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"Call failed: {response.StatusCode} — {err}");
        }

        var responseRaw = await response.Content.ReadAsStringAsync();
        var json = ExtractJson(responseRaw);
        var result = JsonSerializer.Deserialize<JsonElement?>(json);
        if (result is null)
            return "[]";

        var resProp = result.Value.GetProperty("result");
        if (resProp.TryGetProperty("content", out var contentArr))
        {
            var texts = contentArr.EnumerateArray()
                .Where(c => c.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(c => c.GetProperty("text").GetString() ?? "")
                .ToList();
            return string.Join("\n", texts);
        }

        return result.Value.GetRawText();
    }

    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new Exception("Пустой ответ");

        if (raw.TrimStart().StartsWith("{") || raw.TrimStart().StartsWith("["))
            return raw.Trim();

        var dataIdx = raw.IndexOf("data:");
        if (dataIdx >= 0)
        {
            var json = raw[(dataIdx + 5)..].Trim();
            var lastDataIdx = raw.LastIndexOf("data:");
            if (lastDataIdx > dataIdx)
                json = raw[(lastDataIdx + 5)..].Trim();
            return json;
        }

        var braceIdx = raw.IndexOf('{');
        if (braceIdx >= 0)
            return raw[braceIdx..].Trim();

        throw new Exception($"Не удалось извлечь JSON из: {raw[..Math.Min(100, raw.Length)]}");
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
