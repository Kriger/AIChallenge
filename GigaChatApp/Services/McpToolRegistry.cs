using System.Text.RegularExpressions;

namespace GigaChatApp.Services;

/// <summary>
/// Реестр MCP-инструментов. Регистрирует сервисы и выполняет вызовы.
/// </summary>
public sealed class McpToolRegistry : IAsyncDisposable
{
    private readonly List<McpToolDefinition> _tools = new();
    private static readonly Regex ToolCallRegex = new(
        @"<<tool:(?<name>\w+)>>\s*(?<args>\{[^}]*\})?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// Описание доступного инструмента.
    /// </summary>
    public record McpToolDefinition(
        string Name,
        string Description,
        string? ServiceName,
        string? InputSchema = null,
        Func<string, Task<string>>? Call = null);

    public IReadOnlyList<McpToolDefinition> Tools => _tools;

    /// <summary>
    /// Регистрирует инструмент из MCP-сервиса.
    /// </summary>
    public void Register(McpToolDefinition definition)
    {
        _tools.Add(definition);
    }

    /// <summary>
    /// Парсит tool-вызовы из ответа LLM.
    /// </summary>
    public static List<(string Name, string? Args)> ParseToolCalls(string response)
    {
        var matches = ToolCallRegex.Matches(response);
        return matches.Select(m => (
            Name: m.Groups["name"].Value,
            Args: m.Groups["args"].Success ? m.Groups["args"].Value : null
        )).ToList();
    }

    /// <summary>
    /// Выполняет tool-вызов.
    /// </summary>
    public async Task<string> ExecuteToolCall(string name, string? args)
    {
        var tool = _tools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
            throw new InvalidOperationException($"Инструмент '{name}' не найден. Доступные: {string.Join(", ", _tools.Select(t => t.Name))}");

        return await tool.Call(args);
    }

    /// <summary>
    /// Формирует описание всех инструментов для системного сообщения.
    /// </summary>
    public string GetToolsPrompt()
    {
        if (_tools.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("=== ДОСТУПНЫЕ ИНСТРУМЕНТЫ ===");
        sb.AppendLine("Ты можешь вызывать внешние инструменты для выполнения задач.");
        sb.AppendLine();
        sb.AppendLine("Формат вызова инструмента:");
        sb.AppendLine("  <<tool:имя_инструмента>>");
        sb.AppendLine("  {\"аргумент\": \"значение\"}");
        sb.AppendLine();
        sb.AppendLine("Пример:");
        sb.AppendLine("  <<tool:create_todo_item>>");
        sb.AppendLine("  {\"title\": \"Купить хлеб\", \"description\": \"Срочно\"}");
        sb.AppendLine();
        sb.AppendLine("Инструменты:");

        foreach (var tool in _tools)
        {
            sb.AppendLine($"  • **{tool.Name}** — {tool.Description}");
            if (!string.IsNullOrWhiteSpace(tool.InputSchema))
            {
                try
                {
                    var schemaTrimmed = tool.InputSchema.Trim();
                    if (schemaTrimmed.Length > 2 && schemaTrimmed != "{}")
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(schemaTrimmed);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("properties", out var props) && props.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            var requiredParams = new List<string>();
                            if (root.TryGetProperty("required", out var req) && req.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                requiredParams = req.EnumerateArray()
                                    .Select(r => r.GetString() ?? "")
                                    .Where(r => !string.IsNullOrEmpty(r))
                                    .ToList();
                            }

                            var paramList = new List<string>();
                            foreach (var prop in props.EnumerateObject())
                            {
                                var desc = prop.Value.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "";
                                var reqLabel = requiredParams.Contains(prop.Name) ? " (обязательный)" : "";
                                paramList.Add($"    - `{prop.Name}` ({desc}){reqLabel}");
                            }
                            if (paramList.Count > 0)
                            {
                                sb.AppendLine($"    Параметры:");
                                sb.AppendLine(string.Join("\n", paramList));
                            }
                        }
                    }
                }
                catch
                {
                    // игнорируем невалидные схемы
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("ВАЖНО:");
        sb.AppendLine("1. Если нужно выполнить действие — ОБЯЗАТЕЛЬНО вызови инструмент в указанном формате");
        sb.AppendLine("2. После вызова инструмента — НЕ пиши текст, жди результата");
        sb.AppendLine("3. Результат инструмента будет добавлен в диалог автоматически");
        sb.AppendLine("4. Можно вызвать несколько инструментов подряд");
        sb.AppendLine("=== КОНЕЦ ИНСТРУМЕНТОВ ===");

        return sb.ToString();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
