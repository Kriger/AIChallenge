using GigaChatApp.Models;
using GigaChatApp.Infrastructure;

namespace GigaChatApp.Commands;

public sealed class AgentProfileCommand : CommandHandler
{
    public override string Name => "agent-profile";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            await ExecuteStatusAsync(ctx);
            return true;
        }

        var profileCommand = parts[1].ToLowerInvariant();
        switch (profileCommand)
        {
            case "name":
                await SetStringFieldAsync(p => ctx.Agent.AgentProfile.Name = p, parts, "имя", ctx);
                break;

            case "style":
                await SetEnumFieldAsync<CommunicationStyle>(ctx.Agent.AgentProfile.Style,
                    v => ctx.Agent.AgentProfile.Style = v, parts, "style", "concise, detailed, balanced", ctx);
                break;

            case "format":
                await SetEnumFieldAsync<OutputFormat>(ctx.Agent.AgentProfile.Format,
                    v => ctx.Agent.AgentProfile.Format = v, parts, "format", "markdown, plaintext, codeonly, structured", ctx);
                break;

            case "language":
                await SetEnumFieldAsync<ResponseLanguage>(ctx.Agent.AgentProfile.Language,
                    v => ctx.Agent.AgentProfile.Language = v, parts, "language", "russian, english, auto", ctx);
                break;

            case "depth":
                await SetEnumFieldAsync<ExpertiseLevel>(ctx.Agent.AgentProfile.Depth,
                    v => ctx.Agent.AgentProfile.Depth = v, parts, "depth", "beginner, intermediate, expert", ctx);
                break;

            case "domain":
                await SetStringFieldAsync(p => ctx.Agent.AgentProfile.Domain = p, parts, "домен", ctx);
                break;

            case "tech":
                await ExecuteTechAsync(parts, ctx);
                break;

            case "constraint":
                await AddToListAsync(ctx.Agent.AgentProfile.ResponseConstraints, parts, "Ограничение", ctx);
                break;

            case "req":
            case "requirement":
                await AddToListAsync(ctx.Agent.AgentProfile.ResponseRequirements, parts, "Требование", ctx);
                break;

            case "instructions":
                await SetStringFieldAsync(p => ctx.Agent.AgentProfile.Instructions = p, parts, "инструкции", ctx);
                break;

            case "reset":
                ctx.Agent.AgentProfile = AgentProfileManager.Load("default");
                AgentProfileManager.Save(ctx.Agent.AgentProfile);
                PrintGreen("✅ Профиль агента сброшен к значениям по умолчанию");
                Console.WriteLine();
                break;

            default:
                PrintRed($"❌ Неизвестная команда профиля: {profileCommand}. Введи /profile для подсказки.");
                Console.WriteLine();
                break;
        }
        return true;
    }

    private async Task ExecuteStatusAsync(CommandContext ctx)
    {
        PrintYellow("🤖 Профиль агента:");
        var p = ctx.Agent.AgentProfile;
        Console.WriteLine($"   Имя: {p.Name}");
        Console.WriteLine($"   Стиль: {p.Style}");
        Console.WriteLine($"   Формат: {p.Format}");
        Console.WriteLine($"   Язык: {p.Language}");
        Console.WriteLine($"   Глубина: {p.Depth}");
        Console.WriteLine($"   Домен: {(string.IsNullOrWhiteSpace(p.Domain) ? "(не задан)" : p.Domain)}");
        Console.WriteLine($"   Предпочитаемые технологии: {(p.PreferredTechnologies.Count == 0 ? "(нет)" : string.Join(", ", p.PreferredTechnologies))}");
        Console.WriteLine($"   Избегаемые технологии: {(p.AvoidedTechnologies.Count == 0 ? "(нет)" : string.Join(", ", p.AvoidedTechnologies))}");
        Console.WriteLine($"   Макс. длина ответа: {(p.MaxResponseLength == 0 ? "(нет)" : p.MaxResponseLength + " слов")}");
        Console.WriteLine($"   Ограничения: {(p.ResponseConstraints.Count == 0 ? "(нет)" : string.Join(", ", p.ResponseConstraints))}");
        Console.WriteLine($"   Требования: {(p.ResponseRequirements.Count == 0 ? "(нет)" : string.Join(", ", p.ResponseRequirements))}");
        Console.WriteLine($"   Инструкции: {(string.IsNullOrWhiteSpace(p.Instructions) ? "(нет)" : p.Instructions)}");
        Console.WriteLine();
        Console.WriteLine("   Команды:");
        Console.WriteLine("   /agent-profile name <имя> — изменить имя");
        Console.WriteLine("   /agent-profile style <concise|detailed|balanced> — стиль общения");
        Console.WriteLine("   /agent-profile format <markdown|plaintext|codeonly|structured> — формат ответов");
        Console.WriteLine("   /agent-profile language <russian|english|auto> — язык ответов");
        Console.WriteLine("   /agent-profile depth <beginner|intermediate|expert> — глубина ответов");
        Console.WriteLine("   /agent-profile domain <домен> — доменная область");
        Console.WriteLine("   /agent-profile tech add|remove|list|clear <технология> — технологии");
        Console.WriteLine("   /agent-profile constraint <текст> — ограничение в ответе");
        Console.WriteLine("   /agent-profile req <текст> — обязательный элемент ответа");
        Console.WriteLine("   /agent-profile instructions <текст> — дополнительные инструкции");
        Console.WriteLine("   /agent-profile reset — сбросить профиль к значениям по умолчанию");
        Console.WriteLine();
    }

    private async Task ExecuteTechAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed("❌ Формат: /agent-profile tech <add|remove|list|clear> [технология]");
            Console.WriteLine();
            return;
        }

        var techAction = parts[2].ToLowerInvariant();
        switch (techAction)
        {
            case "add":
                if (parts.Length < 4)
                {
                    PrintRed("❌ Формат: /agent-profile tech add <технология>");
                    Console.WriteLine();
                    return;
                }
                var techToAdd = string.Join(" ", parts.Skip(3));
                if (!ctx.Agent.AgentProfile.PreferredTechnologies.Contains(techToAdd, StringComparer.OrdinalIgnoreCase))
                {
                    ctx.Agent.AgentProfile.PreferredTechnologies.Add(techToAdd);
                    AgentProfileManager.Save(ctx.Agent.AgentProfile);
                    PrintGreen($"✅ Добавлена предпочитаемая технология: {techToAdd}");
                }
                else
                {
                    PrintYellow($"⚠️  Технология '{techToAdd}' уже есть в списке");
                }
                Console.WriteLine();
                break;

            case "remove":
                if (parts.Length < 4)
                {
                    PrintRed("❌ Формат: /agent-profile tech remove <технология>");
                    Console.WriteLine();
                    return;
                }
                var techToRemove = string.Join(" ", parts.Skip(3));
                var countBefore = ctx.Agent.AgentProfile.PreferredTechnologies.Count;
                ctx.Agent.AgentProfile.PreferredTechnologies.RemoveAll(t => t.Equals(techToRemove, StringComparison.OrdinalIgnoreCase));
                if (ctx.Agent.AgentProfile.PreferredTechnologies.Count < countBefore)
                {
                    AgentProfileManager.Save(ctx.Agent.AgentProfile);
                    PrintGreen($"✅ Удалена предпочитаемая технология: {techToRemove}");
                }
                else
                {
                    PrintYellow($"⚠️  Технология '{techToRemove}' не найдена в списке");
                }
                Console.WriteLine();
                break;

            case "list":
                PrintYellow("📦 Предпочитаемые технологии:");
                if (ctx.Agent.AgentProfile.PreferredTechnologies.Count == 0)
                {
                    Console.WriteLine("   (пусто)");
                }
                else
                {
                    foreach (var tech in ctx.Agent.AgentProfile.PreferredTechnologies)
                        Console.WriteLine($"   • {tech}");
                }
                Console.WriteLine();
                break;

            case "clear":
                ctx.Agent.AgentProfile.PreferredTechnologies.Clear();
                AgentProfileManager.Save(ctx.Agent.AgentProfile);
                PrintGreen("✅ Список предпочитаемых технологий очищен");
                Console.WriteLine();
                break;

            default:
                PrintRed($"❌ Неизвестное действие: {techAction}. Доступны: add, remove, list, clear");
                Console.WriteLine();
                break;
        }
    }

    private async Task SetStringFieldAsync(Action<string> setter, string[] parts, string fieldName, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed($"❌ Формат: /agent-profile {fieldName} <значение>");
            Console.WriteLine();
            return;
        }
        var value = string.Join(" ", parts.Skip(2));
        setter(value);
        AgentProfileManager.Save(ctx.Agent.AgentProfile);
        PrintGreen($"✅ {fieldName.Capitalize()} изменён на: {value}");
        Console.WriteLine();
    }

    private async Task SetEnumFieldAsync<T>(T current, Action<T> setter, string[] parts, string fieldName, string available, CommandContext ctx) where T : struct, Enum
    {
        if (parts.Length < 3)
        {
            PrintRed($"❌ Формат: /agent-profile {fieldName} <{available}>");
            Console.WriteLine();
            return;
        }
        if (Enum.TryParse<T>(parts[2], ignoreCase: true, out var newValue))
        {
            setter(newValue);
            AgentProfileManager.Save(ctx.Agent.AgentProfile);
            PrintGreen($"✅ {fieldName.Capitalize()} изменён на: {newValue}");
            Console.WriteLine();
        }
        else
        {
            PrintRed($"❌ Неизвестный {fieldName}: '{parts[2]}'. Доступны: {available}");
            Console.WriteLine();
        }
    }

    private async Task AddToListAsync(List<string> list, string[] parts, string itemName, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed($"❌ Формат: /agent-profile {itemName.ToLower()} <текст>");
            Console.WriteLine();
            return;
        }
        var text = string.Join(" ", parts.Skip(2));
        list.Add(text);
        AgentProfileManager.Save(ctx.Agent.AgentProfile);
        PrintGreen($"✅ {itemName} добавлено: {text}");
        Console.WriteLine();
    }
}

public static class StringExtensions
{
    public static string Capitalize(this string s) =>
        s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
