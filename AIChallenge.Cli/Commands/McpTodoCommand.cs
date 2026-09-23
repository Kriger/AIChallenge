using AIChallenge.Services;
using System.Text.Json;

namespace AIChallenge.Cli.Commands;

/// <summary>
/// Команда для работы с TodoMCP (Todoшница).
/// </summary>
public sealed class McpTodoCommand : CommandHandler
{
    public override string Name => "todo";

    public McpTodoService? TodoService { get; set; }

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (TodoService is null)
        {
            PrintRed("❌ TodoMCP не инициализирован. Проверьте appsettings.json.");
            Console.WriteLine();
            return true;
        }

        if (parts.Length < 2)
        {
            PrintYellow("📋 Использование TodoMCP:");
            Console.WriteLine();
            PrintCmd("todo", "connect", "подключиться к TodoMCP");
            PrintCmd("todo", "tools", "список инструментов");
            PrintCmd("todo", "call <tool> <json>", "вызвать инструмент");
            PrintCmd("todo", "list", "список задач (list_todo_items)");
            PrintCmd("todo", "status", "статус подключения");
            Console.WriteLine();
            return true;
        }

        var subcommand = parts[1].ToLowerInvariant();

        switch (subcommand)
        {
            case "connect":
                await HandleConnectAsync();
                break;

            case "tools":
                HandleTools();
                break;

            case "call":
                if (parts.Length < 3)
                {
                    PrintRed("❌ Укажите инструмент: /todo call <tool> [json-args]");
                    Console.WriteLine();
                    return true;
                }
                await HandleCallAsync(parts);
                break;

            case "list":
                await HandleListAsync();
                break;

            case "status":
                HandleStatus();
                break;

            default:
                PrintRed($"❌ Неизвестная подкоманда: {subcommand}");
                Console.WriteLine();
                break;
        }

        return true;
    }

    private async Task HandleConnectAsync()
    {
        try
        {
            PrintCyan("🔌 Подключение к TodoMCP...");
            await TodoService!.ConnectAsync();
            PrintGreen("✅ Подключено!");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            PrintRed($"❌ Ошибка: {ex.Message}");
            Console.WriteLine();
        }
    }

    private void HandleTools()
    {
        if (!TodoService!.IsConnected)
        {
            PrintRed("❌ Не подключено. Сначала /todo connect");
            Console.WriteLine();
            return;
        }

        PrintCyan(TodoService.GetToolsDescription());
    }

    private async Task HandleCallAsync(string[] parts)
    {
        if (!TodoService!.IsConnected)
        {
            PrintRed("❌ Не подключено. Сначала /todo connect");
            Console.WriteLine();
            return;
        }

        var toolName = parts[2];
        JsonElement? arguments = null;

        // Аргументы опциональны: /todo call get_current_user
        if (parts.Length >= 4)
        {
            var argsJson = parts[3].Trim();

            // Убираем одинарные кавычки оболочки: '{"key":"val"}' → {"key":"val"}
            if (argsJson.StartsWith("'") && argsJson.EndsWith("'"))
                argsJson = argsJson[1..^1];

            try
            {
                var doc = JsonDocument.Parse(argsJson);
                arguments = doc.RootElement;
            }
            catch (Exception ex)
            {
                PrintRed($"❌ Ошибка JSON: {ex.Message}");
                Console.WriteLine();
                return;
            }
        }

        PrintCyan($"⚡ {toolName}");
        if (arguments is not null)
            PrintGray($"   {arguments.Value.GetRawText()}");
        else
            PrintGray("   (без аргументов)");
        Console.WriteLine();

        try
        {
            var dict = arguments.HasValue
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.Value.GetRawText())
                : null;
            var result = await TodoService.CallToolAsync(toolName, dict);
            PrintGreen($"✅ {toolName}:");
            Console.WriteLine(result);
        }
        catch (Exception ex)
        {
            PrintRed($"❌ {ex.Message}");
        }

        Console.WriteLine();
    }

    private async Task HandleListAsync()
    {
        if (!TodoService!.IsConnected)
        {
            PrintRed("❌ Не подключено. Сначала /todo connect");
            Console.WriteLine();
            return;
        }

        PrintCyan("📋 Загрузка задач...");
        try
        {
            var result = await TodoService.CallToolAsync("list_todo_items", null);
            PrintGreen("✅ Задачи:");
            Console.WriteLine(result);
        }
        catch (Exception ex)
        {
            PrintRed($"❌ {ex.Message}");
        }

        Console.WriteLine();
    }

    private void HandleStatus()
    {
        var status = TodoService!.IsConnected
            ? $"✅ Подключено | Инструментов: {TodoService.Tools.Count}"
            : "❌ Не подключено";

        PrintCyan($"📊 Статус: {status}");
        Console.WriteLine();
    }

}
