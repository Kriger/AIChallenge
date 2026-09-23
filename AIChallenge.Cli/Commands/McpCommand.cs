using AIChallenge.Services;
using System.Text.Json;

namespace AIChallenge.Cli.Commands;

/// <summary>
/// Команда для работы с GitHub через MCP.
/// Поддерживает: /mcp connect, /mcp tools, /mcp call &lt;tool&gt; &lt;args&gt;
/// </summary>
public sealed class McpCommand : CommandHandler
{
    public override string Name => "mcp";

    public McpGitHubService? McpService { get; set; }

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (McpService is null)
        {
            PrintRed("❌ MCP-сервис не инициализирован. Проверьте конфигурацию appsettings.json.");
            Console.WriteLine();
            return true;
        }

        if (parts.Length < 2)
        {
            PrintYellow("🔌 Использование MCP GitHub:");
            Console.WriteLine();
            PrintCmd("connect", "подключиться к MCP GitHub");
            PrintCmd("tools", "показать список инструментов");
            PrintCmd("call <tool> <json-args>", "вызвать инструмент");
            PrintCmd("status", "статус подключения");
            Console.WriteLine();
            return true;
        }

        var subcommand = parts[1].ToLowerInvariant();

        switch (subcommand)
        {
            case "connect":
                await HandleConnectAsync(ctx);
                break;

            case "tools":
                HandleTools(ctx);
                break;

            case "call":
                if (parts.Length < 3)
                {
                    PrintRed("❌ Укажите инструмент: /mcp call <tool> [json-args]");
                    Console.WriteLine();
                    return true;
                }
                await HandleCallAsync(parts, ctx);
                break;

            case "status":
                HandleStatus(ctx);
                break;

            default:
                PrintRed($"❌ Неизвестная подкоманда: {subcommand}");
                Console.WriteLine();
                break;
        }

        return true;
    }

    private async Task HandleConnectAsync(CommandContext ctx)
    {
        try
        {
            PrintCyan("🔌 Подключение к MCP GitHub...");
            await McpService!.ConnectAsync();
            PrintGreen("✅ Подключено успешно!");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            PrintRed($"❌ Ошибка подключения: {ex.Message}");
            Console.WriteLine();
        }
    }

    private void HandleTools(CommandContext ctx)
    {
        if (!McpService!.IsConnected)
        {
            PrintRed("❌ Не подключено. Сначала выполните /mcp connect");
            Console.WriteLine();
            return;
        }

        var description = McpService.GetToolsDescription();
        PrintCyan(description);
    }

    private async Task HandleCallAsync(string[] parts, CommandContext ctx)
    {
        if (!McpService!.IsConnected)
        {
            PrintRed("❌ Не подключено. Сначала выполните /mcp connect");
            Console.WriteLine();
            return;
        }

        var toolName = parts[2];
        Dictionary<string, object?>? arguments = null;

        // Аргументы опциональны: /mcp call list-commits
        if (parts.Length >= 4)
        {
            var argsJson = parts[3].Trim();

            // Убираем одинарные кавычки оболочки: '{"key":"val"}' → {"key":"val"}
            if (argsJson.StartsWith("'") && argsJson.EndsWith("'"))
                argsJson = argsJson[1..^1];

            try
            {
                arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson)
                    ?? throw new Exception("Некорректный JSON");
            }
            catch (Exception ex)
            {
                PrintRed($"❌ Ошибка парсинга аргументов: {ex.Message}");
                Console.WriteLine("   Пример: /mcp call list-commits '{\"owner\":\"octocat\",\"repo\":\"Hello-World\"}'");
                Console.WriteLine();
                return;
            }
        }

        PrintCyan($"⚡ Вызов инструмента: {toolName}");
        if (arguments is not null && arguments.Count > 0)
            PrintGray($"   Аргументы: {JsonSerializer.Serialize(arguments)}");
        else
            PrintGray("   (без аргументов)");
        Console.WriteLine();

        try
        {
            var result = await McpService!.CallToolAsync(toolName, arguments);
            PrintGreen($"✅ Результат {toolName}:");
            Console.WriteLine(result);
        }
        catch (Exception ex)
        {
            PrintRed($"❌ Ошибка вызова {toolName}: {ex.Message}");
        }

        Console.WriteLine();
    }

    private void HandleStatus(CommandContext ctx)
    {
        var status = McpService!.IsConnected
            ? $"✅ Подключено | Инструментов: {McpService.Tools.Count}"
            : "❌ Не подключено";

        PrintCyan($"📊 Статус MCP: {status}");
        Console.WriteLine();
    }

    private static void PrintCmd(string cmd, string desc)
    {
        Console.Write($"   /mcp {cmd,-45}");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"— {desc}");
        Console.ResetColor();
    }

    private static void PrintCyan(string text)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void PrintGreen(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void PrintRed(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void PrintGray(string text)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
