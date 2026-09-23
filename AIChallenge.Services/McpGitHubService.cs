using AIChallenge.Models;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Collections;
using System.Text.Json;

namespace AIChallenge.Services;

/// <summary>
/// Сервис для работы с GitHub через MCP.
/// </summary>
public sealed class McpGitHubService : IAsyncDisposable
{
    private readonly Action<string, LogLevel> _log;
    private readonly IConfiguration _configuration;
    private McpClient? _client;
    private bool _connected;
    private List<McpClientTool>? _tools;

    public bool IsConnected => _connected;
    public IReadOnlyList<McpClientTool> Tools => _tools ?? [];

    /// <summary>
    /// Создаёт сервис с делегатом логирования.
    /// </summary>
    public McpGitHubService(Action<string, LogLevel> log, IConfiguration configuration)
    {
        _log = log;
        _configuration = configuration;
    }

    /// <summary>
    /// Подключиться к MCP-серверу GitHub.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_connected)
            return;

        var mcpConfig = _configuration.GetSection("Mcp");
        var mcpEnabled = mcpConfig["Enabled"] == "true";
        if (!mcpEnabled)
        {
            Log("MCP отключён в конфигурации", LogLevel.Warning);
            return;
        }

        var githubSection = mcpConfig.GetSection("Servers:GitHub");
        if (!githubSection.Exists())
        {
            Log("Конфигурация MCP GitHub не найдена в appsettings.json", LogLevel.Warning);
            return;
        }

        var command = githubSection["Command"] ?? "npx";

        // Правильно читаем массив Arguments из JSON
        var args = githubSection.GetSection("Arguments")
            .GetChildren()
            .Select(c => c.Value ?? string.Empty)
            .ToArray();
        if (args.Length == 0)
        {
            // Fallback: читаем как строку (старый формат)
            var argsStr = githubSection["Arguments"] ?? string.Empty;
            Log($"⚠️ Arguments как массив пуст, fallback: '{argsStr}'", LogLevel.Warning);
            args = string.IsNullOrEmpty(argsStr)
                ? Array.Empty<string>()
                : argsStr.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        // Собираем env vars из конфига
        var envVars = new Dictionary<string, string?>();
        var envSection = githubSection.GetSection("EnvironmentVariables");
        foreach (var prop in envSection.GetChildren())
        {
            envVars[prop.Key] = prop.Value;
        }

        // Добавляем токен из env-переменной, если он пустой в конфиге
        if (envVars.ContainsKey("GITHUB_PERSONAL_ACCESS_TOKEN"))
        {
            if (string.IsNullOrEmpty(envVars["GITHUB_PERSONAL_ACCESS_TOKEN"]))
            {
                var envToken = Environment.GetEnvironmentVariable("GITHUB_PERSONAL_ACCESS_TOKEN");
                if (!string.IsNullOrEmpty(envToken))
                    envVars["GITHUB_PERSONAL_ACCESS_TOKEN"] = envToken;
            }

            // Проверяем, что токен задан
            if (string.IsNullOrEmpty(envVars["GITHUB_PERSONAL_ACCESS_TOKEN"]) ||
                envVars["GITHUB_PERSONAL_ACCESS_TOKEN"] == "ghp_ВАШ_ТОКЕН_ЗДЕСЬ")
            {
                Log("⚠️  GITHUB_PERSONAL_ACCESS_TOKEN не задан! Сервер GitHub MCP не сможет работать.", LogLevel.Warning);
                Log("   1. Создайте токен: https://github.com/settings/tokens", LogLevel.Warning);
                Log("   2. Права: repo, read:user", LogLevel.Warning);
                Log("   3. Внесите в appsettings.json → Mcp → Servers → GitHub → EnvironmentVariables", LogLevel.Warning);
            }
        }

        // Копируем текущие env vars, чтобы серверу были доступны PATH и т.д.
        foreach (DictionaryEntry kv in Environment.GetEnvironmentVariables())
        {
            if (kv.Key is string key && kv.Value is string val && !envVars.ContainsKey(key))
                envVars[key] = val;
        }

        var transport = new StdioClientTransport(new()
        {
            Name = "github-mcp",
            Command = command,
            Arguments = args,
            EnvironmentVariables = envVars,
        });

        try
        {
            Log($"⏳ Запуск MCP-сервера (ожидание до 30с)...", LogLevel.Info);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _client = await McpClient.CreateAsync(transport, null, null, cts.Token);
            _connected = true;

            // Загружаем список инструментов
            var toolsResult = await _client.ListToolsAsync();
            _tools = toolsResult.ToList();

            Log($"✅ MCP GitHub подключён. Доступно инструментов: {_tools.Count}", LogLevel.Info);
        }
        catch (OperationCanceledException)
        {
            Log($"❌ Таймаут подключения (30с). npx может скачивать пакет при первом запуске.", LogLevel.Error);
            Log($"   Попробуйте ещё раз через 10-30 секунд.", LogLevel.Warning);
            throw;
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка подключения к MCP GitHub: {ex.Message}", LogLevel.Error);
            if (ex.InnerException != null)
                Log($"   Внутренняя ошибка: {ex.InnerException.Message}", LogLevel.Warning);
            throw;
        }
    }

    /// <summary>
    /// Вызвать инструмент MCP.
    /// </summary>
    public async Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        if (!_connected || _client is null)
            throw new InvalidOperationException("MCP-клиент не подключён. Вызовите ConnectAsync() сначала.");

        try
        {
            Log($"Вызов инструмента MCP: {toolName}", LogLevel.Info);
            var result = await _client.CallToolAsync(toolName, arguments);

            var content = string.Join("\n", result.Content.OfType<ContentBlock>()
                .Where(c => c.Type == "text")
                .Select(c => c.ToString()));

            Log($"Инструмент {toolName} выполнен успешно", LogLevel.Info);

            if (string.IsNullOrWhiteSpace(content))
                return result.Content.Select(c => c.ToString()).FirstOrDefault() ?? "(пустой результат)";

            // Форматируем JSON-ответы для читаемости
            return FormatJson(content);
        }
        catch (Exception ex)
        {
            Log($"Ошибка вызова инструмента {toolName}: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    /// <summary>
    /// Форматирует JSON: Unicode → UTF-8, pretty-print.
    /// </summary>
    private static string FormatJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        raw = raw.Trim();

        // Если это JSON — форматируем
        if (raw.StartsWith("[") || raw.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var formatted = doc.RootElement.GetRawText();
                return System.Text.RegularExpressions.Regex.Unescape(formatted);
            }
            catch
            {
                return System.Text.RegularExpressions.Regex.Unescape(raw);
            }
        }

        // Plain text — декодируем Unicode
        return System.Text.RegularExpressions.Regex.Unescape(raw);
    }

    /// <summary>
    /// Получить описание списка инструментов GitHub MCP.
    /// </summary>
    public string GetToolsDescription()
    {
        if (!_connected || _tools is null)
            return "MCP GitHub не подключён.";

        var sb = new StringBuilder();
        sb.AppendLine($"📦 Инструменты GitHub MCP ({_tools.Count}):");
        foreach (var tool in _tools)
        {
            sb.AppendLine($"  • **{tool.Name}**");
            sb.AppendLine($"    {tool.Description}");
            if (tool.JsonSchema.ValueKind != JsonValueKind.Null)
            {
                sb.AppendLine($"    Параметры: {tool.JsonSchema}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private void Log(string message, LogLevel level)
    {
        _log?.Invoke(message, level);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _connected = false;
        }
    }
}
