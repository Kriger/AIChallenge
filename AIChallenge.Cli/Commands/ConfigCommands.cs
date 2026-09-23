using AIChallenge.Models;
using AIChallenge.Core.Infrastructure;

namespace AIChallenge.Cli.Commands;

public sealed class StatusCommand : CommandHandler
{
    public override string Name => "status";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        PrintYellow("📋 Текущие настройки агента:");
        Console.WriteLine($"   Модель: {ctx.Agent.Model}");
        Console.WriteLine($"   Temperature: {ctx.Agent.Temperature ?? (object)"(не задано)"}");
        Console.WriteLine($"   MaxTokens: {ctx.Agent.MaxTokens}");
        Console.WriteLine($"   StopSequences: [{string.Join(", ", ctx.Agent.StopSequences.Select(s => $"\"{s}\""))}]");
        Console.WriteLine($"   SystemMessage: {ctx.Agent.SystemMessage}");
        Console.WriteLine();

        // Профиль агента
        PrintCyan("🤖 Профиль агента:");
        var prof = ctx.Agent.AgentProfile;
        Console.WriteLine($"   Имя: {prof.Name}");
        if (!string.IsNullOrWhiteSpace(prof.Role))
            Console.WriteLine($"   Роль: {prof.Role}");
        Console.WriteLine($"   Стиль: {prof.Style} | Формат: {prof.Format} | Язык: {prof.Language}");
        Console.WriteLine($"   Глубина: {prof.Depth} | Домен: {(string.IsNullOrWhiteSpace(prof.Domain) ? "(не задан)" : prof.Domain)}");
        if (prof.PreferredTechnologies.Count > 0)
            Console.WriteLine($"   Технологии: {string.Join(", ", prof.PreferredTechnologies)}");
        Console.WriteLine();

        // Статус контекста
        PrintCyan("📦 Управление контекстом:");
        var cmStatus = ctx.Agent.ContextManager;
        var cfgStatus = cmStatus.Config;
        Console.WriteLine($"   Включено: {(cfgStatus.Enabled ? "да" : "нет")}");
        Console.WriteLine($"   Recent: {cfgStatus.RecentMessageCount}, Interval: {cfgStatus.SummaryInterval}");
        Console.WriteLine($"   Summary блоков: {cmStatus.SummaryCount}");
        Console.WriteLine();

        // Метрики
        Console.WriteLine("📈 Метрики:");
        var m = ctx.Agent.Metrics;
        Console.WriteLine($"   Запросов: {m.TotalRequests}  |  Успешно: {m.SuccessfulRequests}  |  Ошибок: {m.FailedRequests}");
        Console.WriteLine($"   Из кэша: {m.CachedRequests}  |  Retry: {m.RetryAttempts}");
        Console.WriteLine($"   Success rate: {m.SuccessRate:P1}");
        Console.WriteLine($"   Avg duration: {m.TotalDuration.TotalMilliseconds:F0} мс");
        Console.WriteLine($"   Токены: {m.TotalPromptTokens} in → {m.TotalCompletionTokens} out");
        Console.WriteLine($"   Токены контекста: {m.TotalContextTokens} всего, {m.LastContextTokens} последний");
        Console.WriteLine($"   Кэш: {ctx.Agent.Cache.Count} записей");
        Console.WriteLine();

        // Память
        PrintMagenta("🧠 Память:");
        Console.WriteLine($"   Краткосрочная (диалог): {ctx.Agent.MemoryManager.ShortTerm.Count} записей");
        Console.WriteLine($"   Рабочая (задача):       {ctx.Agent.MemoryManager.Working.Count} записей, задача: {ctx.Agent.MemoryManager.Working.CurrentTaskId ?? "нет"}");
        Console.WriteLine($"   Долгосрочная (знания):  {ctx.Agent.MemoryManager.LongTerm.Count} фактов");
        Console.WriteLine();

        return true;
    }
}

public sealed class ModelCommand : CommandHandler
{
    public override string Name => "model";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        PrintYellow("📦 Доступные модели:");
        foreach (var kvp in AvailableModels.Models)
        {
            var marker = kvp.Value == ctx.Agent.Model ? " ▶" : "";
            Console.WriteLine($"   {kvp.Key}. {kvp.Value}{marker}");
        }
        Console.WriteLine();
        Console.Write("   Введите номер или название: ");
        Console.ResetColor();

        var modelInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(modelInput))
        {
            if (int.TryParse(modelInput, out var modelNumber) && AvailableModels.Models.ContainsKey(modelNumber))
            {
                ctx.Agent.Model = AvailableModels.Models[modelNumber];
                ctx.Config.Model = ctx.Agent.Model;
                PrintGreen($"✅ Модель изменена на: {ctx.Agent.Model}");
            }
            else if (AvailableModels.Models.ContainsValue(modelInput))
            {
                ctx.Agent.Model = modelInput;
                ctx.Config.Model = ctx.Agent.Model;
                PrintGreen($"✅ Модель изменена на: {ctx.Agent.Model}");
            }
            else
            {
                PrintRed($"❌ Модель '{modelInput}' не найдена.");
            }
        }
        Console.WriteLine();
        return true;
    }
}

public sealed class SystemCommand : CommandHandler
{
    public override string Name => "system";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            PrintRed("❌ Укажите новое системное сообщение: /system <текст>");
            Console.WriteLine();
            return true;
        }
        ctx.Agent.SystemMessage = parts[1];
        ctx.Config.SystemMessage = parts[1];
        PrintGreen("✅ SystemMessage обновлён.");
        Console.WriteLine();
        return true;
    }
}

public sealed class MaxTokensCommand : CommandHandler
{
    public override string Name => "maxtokens";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var maxTokens))
        {
            PrintRed("❌ Укажите число: /maxtokens <число>");
            Console.WriteLine();
            return true;
        }
        ctx.Agent.MaxTokens = maxTokens;
        ctx.Config.MaxTokens = maxTokens;
        PrintGreen($"✅ MaxTokens установлен в {maxTokens}.");
        Console.WriteLine();
        return true;
    }
}

public sealed class StopCommand : CommandHandler
{
    public override string Name => "stop";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            PrintRed("❌ Укажите стоп-последовательности через запятую: /stop <seq1,seq2,...>");
            Console.WriteLine();
            return true;
        }
        ctx.Agent.StopSequences = parts[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        ctx.Config.StopSequences = ctx.Agent.StopSequences;
        PrintGreen($"✅ StopSequences обновлены: [{string.Join(", ", ctx.Agent.StopSequences.Select(s => $"\"{s}\""))}]");
        Console.WriteLine();
        return true;
    }
}

public sealed class TempCommand : CommandHandler
{
    public override string Name => "temp";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            PrintRed("❌ Укажите число: /temp <0-2>. Пример: /temp 0.7");
            Console.WriteLine("   /temp clear — сбросить ограничение.");
            Console.WriteLine();
            return true;
        }

        if (parts[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Agent.Temperature = null;
            ctx.Config.Temperature = null;
            PrintGreen("✅ Temperature сброшен.");
            Console.WriteLine();
            return true;
        }

        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var temp))
        {
            PrintRed("❌ Укажите число: /temp <0-2>. Пример: /temp 0.7");
            Console.WriteLine();
            return true;
        }

        if (temp < 0 || temp > 2)
        {
            PrintRed("❌ Temperature должно быть в диапазоне 0–2.");
            Console.WriteLine();
            return true;
        }

        ctx.Agent.Temperature = temp;
        ctx.Config.Temperature = temp;
        PrintGreen($"✅ Temperature установлен в {temp}.");
        Console.WriteLine();
        return true;
    }
}

public sealed class RetryCommand : CommandHandler
{
    public override string Name => "retry";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var retryCount))
        {
            PrintRed("❌ Укажите число: /retry <count>");
            Console.WriteLine();
            return true;
        }
        ctx.Agent.Metrics.RetryCount = retryCount;
        PrintGreen($"✅ RetryCount установлен в {retryCount}.");
        Console.WriteLine();
        return true;
    }
}

public sealed class RetryDelayCommand : CommandHandler
{
    public override string Name => "retrydelay";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var retryDelay))
        {
            PrintRed("❌ Укажите число: /retrydelay <ms>");
            Console.WriteLine();
            return true;
        }
        ctx.Agent.Metrics.RetryDelayMs = retryDelay;
        PrintGreen($"✅ RetryDelayMs установлен в {retryDelay}.");
        Console.WriteLine();
        return true;
    }
}

public sealed class LogLevelCommand : CommandHandler
{
    public override string Name => "loglevel";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2 || !Enum.TryParse(parts[1], ignoreCase: true, out LogLevel logLevel))
        {
            PrintRed("❌ Укажите уровень: /loglevel [debug|info|warning|error]");
            Console.WriteLine();
            return true;
        }
        ctx.Agent.Logger.SetMinLevel(logLevel);
        PrintGreen($"✅ Уровень логирования: {logLevel}");
        Console.WriteLine();
        return true;
    }
}

public sealed class MetricsCommand : CommandHandler
{
    public override string Name => "metrics";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        PrintYellow("📊 Метрики агента:");
        var met = ctx.Agent.Metrics;
        Console.WriteLine($"   Запросов: {met.TotalRequests}");
        Console.WriteLine($"   Успешно: {met.SuccessfulRequests}");
        Console.WriteLine($"   Ошибок: {met.FailedRequests}");
        Console.WriteLine($"   Из кэша: {met.CachedRequests}");
        Console.WriteLine($"   Retry: {met.RetryAttempts}");
        Console.WriteLine($"   Success rate: {met.SuccessRate:P1}");
        Console.WriteLine($"   Avg duration: {met.TotalDuration.TotalMilliseconds:F0} мс");
        Console.WriteLine($"   Токены: {met.TotalPromptTokens} in → {met.TotalCompletionTokens} out");
        Console.WriteLine($"   Токены контекста: {met.TotalContextTokens} всего, {met.LastContextTokens} последний");
        Console.WriteLine($"   Кэш: {ctx.Agent.Cache.Count} записей");
        Console.WriteLine($"   Память: {ctx.Agent.MemoryManager.ShortTerm.Count} кратк. | {ctx.Agent.MemoryManager.Working.Count} рабоч. | {ctx.Agent.Memory.Count} длг.");
        Console.WriteLine();

        if (met.ContextComparison.ComparisonCount > 0)
        {
            PrintGreen("📦 Метрики управления контекстом:");
            Console.WriteLine(met.ContextComparison.GetReport());
        }
        return true;
    }
}
