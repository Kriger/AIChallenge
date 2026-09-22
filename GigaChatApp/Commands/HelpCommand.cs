namespace GigaChatApp.Commands;

public sealed class HelpCommand : CommandHandler
{
    public override string Name => "help";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        PrintCyan("📖 Доступные команды:");
        Console.WriteLine();

        PrintYellow("── Настройки ──");
        PrintCmd("status", "текущие настройки");
        PrintCmd("model", "выбор модели");
        PrintCmd("system <текст>", "системное сообщение");
        PrintCmd("maxtokens <число>", "максимальная длина ответа");
        PrintCmd("stop <seq1,seq2>", "стоп-последовательности");
        PrintCmd("temp <0-2>", "температура (0-2)");
        PrintCmd("retry <count>", "количество retry");
        PrintCmd("retrydelay <ms>", "задержка retry");
        PrintCmd("loglevel [debug|info|warning|error]", "уровень логирования");
        PrintCmd("metrics", "статистика");
        Console.WriteLine();

        PrintYellow("── Поведение ──");
        PrintCmd("adaptive", "адаптация (статус/on/off/reset/threshold)");
        PrintCmd("planner", "статус планировщика");
        PrintCmd("plan", "ручное планирование");
        Console.WriteLine();

        PrintYellow("── Память ──");
        PrintCmd("memory <list|save|delete|search|status|extract>", "долгосрочная память");
        PrintCmd("st <list|recent|search|clear>", "краткосрочная память");
        PrintCmd("wt <list|save|delete|task|clear>", "рабочая память");
        Console.WriteLine();

        PrintYellow("── Контекст ──");
        PrintCmd("context <strategy|on|off|reset|recent|interval|report>", "управление контекстом");
        PrintCmd("facts <list|save|delete>", "StickyFacts / Branching");
        PrintCmd("branch <list|create|switch|checkpoint|delete>", "ветвление диалога");
        Console.WriteLine();

        PrintYellow("── Профиль агента ──");
        PrintCmd("agent-profile <name|style|format|language|depth|domain|tech|reset>", "настройки профиля");
        PrintCmd("invariants <add|remove|clear>", "инварианты");
        Console.WriteLine();

        PrintYellow("── FSM ──");
        PrintCmd("fsm <status|questions|answer|next|transition|pause|resume|...>", "управление задачами");
        Console.WriteLine();

        PrintYellow("── MCP GitHub ──");
        PrintCmd("mcp connect", "подключиться к GitHub MCP");
        PrintCmd("mcp tools", "список инструментов");
        PrintCmd("mcp call <tool> <json>", "вызвать инструмент");
        PrintCmd("mcp status", "статус подключения");
        Console.WriteLine();

        PrintYellow("── Прочее ──");
        PrintCmd("save", "сохранить контекст");
        PrintCmd("clear", "очистить историю");
        PrintCmd("skip", "пропустить вопросы");
        Console.WriteLine();

        Console.WriteLine("   quit / exit / q — выход");
        Console.WriteLine();
        return true;
    }

    private static void PrintCmd(string cmd, string desc)
    {
        Console.Write($"   /{cmd,-45}");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"— {desc}");
        Console.ResetColor();
    }
}
