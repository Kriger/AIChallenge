namespace AIChallenge.Cli.Commands;
using AIChallenge.Core.Infrastructure;

public sealed class InvariantsCommand : CommandHandler
{
    public override string Name => "invariants";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            await ExecuteStatusAsync(ctx);
            return true;
        }

        var invCommand = parts[1].ToLowerInvariant();
        switch (invCommand)
        {
            case "add":
                if (parts.Length < 3)
                {
                    PrintRed("❌ Формат: /invariants add <текст инварианта>");
                    Console.WriteLine();
                    return true;
                }
                var invariantText = string.Join(" ", parts.Skip(2));
                ctx.Agent.AgentProfile.Invariants.Add(invariantText);
                AgentProfileManager.Save(ctx.Agent.AgentProfile);
                PrintGreen($"✅ Инвариант добавлен: {invariantText}");
                Console.WriteLine("   ⚠️  Новый инвариант вступит в силу со следующего запроса.");
                Console.WriteLine();
                break;

            case "remove":
                if (parts.Length < 3 || !int.TryParse(parts[2], out var invIndex))
                {
                    PrintRed("❌ Формат: /invariants remove <номер>. Введи /invariants для списка.");
                    Console.WriteLine();
                    return true;
                }
                var invList = ctx.Agent.AgentProfile.Invariants;
                if (invIndex < 1 || invIndex > invList.Count)
                {
                    PrintRed($"❌ Номер вне диапазона (1–{invList.Count}). Введи /invariants для списка.");
                    Console.WriteLine();
                    return true;
                }
                var removed = invList[invIndex - 1];
                invList.RemoveAt(invIndex - 1);
                AgentProfileManager.Save(ctx.Agent.AgentProfile);
                PrintGreen($"✅ Инвариант удалён: {removed}");
                Console.WriteLine();
                break;

            case "clear":
                ctx.Agent.AgentProfile.Invariants.Clear();
                AgentProfileManager.Save(ctx.Agent.AgentProfile);
                PrintGreen("✅ Все инварианты удалены");
                Console.WriteLine();
                break;

            default:
                PrintRed($"❌ Неизвестная команда: {invCommand}. Доступны: add, remove, clear");
                Console.WriteLine();
                break;
        }
        return true;
    }

    private async Task ExecuteStatusAsync(CommandContext ctx)
    {
        PrintYellow("⛔ Инварианты (непреложные правила):");
        var inv = ctx.Agent.AgentProfile.Invariants;
        if (inv.Count == 0)
        {
            Console.WriteLine("   (не заданы)");
        }
        else
        {
            for (int i = 0; i < inv.Count; i++)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"   [{i + 1}] ");
                Console.ResetColor();
                Console.WriteLine(inv[i]);
            }
        }
        Console.WriteLine();
        Console.WriteLine("   Команды:");
        Console.WriteLine("   /invariants add <текст> — добавить инвариант");
        Console.WriteLine("   /invariants remove <номер> — удалить по номеру");
        Console.WriteLine("   /invariants clear — удалить все инварианты");
        Console.WriteLine();
    }
}
