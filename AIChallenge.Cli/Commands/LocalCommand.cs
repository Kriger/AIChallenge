using AIChallenge.Core;
using AIChallenge.Models;

namespace AIChallenge.Cli.Commands;

/// <summary>
/// Управление локальной LLM (Ollama): переключение вкл/выкл, проверка доступности, список моделей.
/// </summary>
public sealed class LocalCommand : CommandHandler
{
    private LocalLlmClient? _client;
    private LocalLlmConfig? _configRef;

    public override string Name => "local";

    private async Task<LocalLlmClient> EnsureClientAsync(CommandContext ctx)
    {
        var needsNew = _client is null || !ReferenceEquals(_configRef, ctx.LocalLlmConfig);
        if (needsNew)
        {
            _client?.Dispose();
            _client = new LocalLlmClient(ctx.LocalLlmConfig);
            _configRef = ctx.LocalLlmConfig;
        }
        return _client;
    }

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        var subCommand = parts.Length > 1 ? parts[1].ToLowerInvariant() : "status";

        switch (subCommand)
        {
            // ─── Статус ───────────────────────────────────────────────
            case "status":
            case "":
                await PrintStatusAsync(ctx);
                break;

            // ─── Включить локальную LLM ───────────────────────────────
            case "on":
                ctx.LocalLlmConfig.Enabled = true;
                ctx.Agent.LocalLlmEnabled = true;
                ctx.Agent.UpdateLocalLlmConfig(ctx.LocalLlmConfig);
                Console.WriteLine($"  ✅ Локальная LLM включена → модель {ctx.LocalLlmConfig.Model}");
                Console.WriteLine($"     URL: {ctx.LocalLlmConfig.Url}");
                Console.WriteLine($"     Все запросы теперь идут в Ollama (без облака).");
                Console.WriteLine();
                break;

            // ─── Выключить локальную LLM (fallback на GigaChat) ──────
            case "off":
                ctx.LocalLlmConfig.Enabled = false;
                ctx.Agent.LocalLlmEnabled = false;
                Console.WriteLine("  ✅ Локальная LLM выключена → запросы идут в GigaChat API.");
                Console.WriteLine();
                break;

            // ─── Переключить (toggle) ─────────────────────────────────
            case "toggle":
            case "switch":
                ctx.LocalLlmConfig.Enabled = !ctx.LocalLlmConfig.Enabled;
                ctx.Agent.LocalLlmEnabled = ctx.LocalLlmConfig.Enabled;
                ctx.Agent.UpdateLocalLlmConfig(ctx.LocalLlmConfig);
                var mode = ctx.LocalLlmConfig.Enabled ? "включена" : "выключена";
                var target = ctx.LocalLlmConfig.Enabled ? "Ollama" : "GigaChat";
                Console.WriteLine($"  ✅ Переключено: локальная LLM {mode} → {target}.");
                Console.WriteLine();
                break;

            // ─── Выбрать модель ───────────────────────────────────────
            case "model":
                await SelectModelAsync(ctx);
                break;

            // ─── Проверить доступность ────────────────────────────────
            case "check":
            case "test":
                await CheckAsync(ctx);
                break;

            // ─── Список моделей ───────────────────────────────────────
            case "models":
            case "list":
                await ListModelsAsync(ctx);
                break;

            // ─── Настройки ────────────────────────────────────────────
            case "url":
                await SetUrlAsync(ctx, parts);
                break;

            case "temp":
                await SetTempAsync(ctx, parts);
                break;

            case "maxtokens":
                await SetMaxTokensAsync(ctx, parts);
                break;

            default:
                Console.WriteLine($"  ⚠️  Неизвестная подкоманда: {subCommand}");
                Console.WriteLine();
                await PrintUsageAsync();
                break;
        }

        return true;
    }

    private async Task PrintStatusAsync(CommandContext ctx)
    {
        Console.WriteLine("  🏠 Локальная LLM (Ollama):");
        Console.WriteLine($"     Включена: {(ctx.LocalLlmConfig.Enabled ? "да ✅" : "нет ❌")}");
        Console.WriteLine($"     Модель: {ctx.LocalLlmConfig.Model}");
        Console.WriteLine($"     URL: {ctx.LocalLlmConfig.Url}");
        Console.WriteLine($"     Temperature: {ctx.LocalLlmConfig.Temperature}");
        Console.WriteLine($"     MaxTokens: {ctx.LocalLlmConfig.MaxTokens}");
        Console.WriteLine($"     Timeout: {ctx.LocalLlmConfig.TimeoutSeconds}с");
        Console.WriteLine();

        if (ctx.LocalLlmConfig.Enabled)
        {
            var client = await EnsureClientAsync(ctx);
            var available = await client.IsAvailableAsync();
            if (available)
            {
                Console.WriteLine("     Статус: подключена ✅");
                var models = await client.ListModelsAsync();
                if (models.Any())
                {
                    Console.WriteLine($"     Загруженные модели: {string.Join(", ", models)}");
                }
            }
            else
            {
                Console.WriteLine($"     Статус: недоступна ❌ (Ollama не отвечает на {ctx.LocalLlmConfig.Url})");
                Console.WriteLine("     Запустите: ollama serve");
            }
        }
        Console.WriteLine();
    }

    private async Task SelectModelAsync(CommandContext ctx)
    {
        Console.WriteLine("  📋 Введите название модели (или 'list' для списка):");
        Console.Write("  → ");
        var input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input)) return;

        if (input.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            await ListModelsAsync(ctx);
            return;
        }

        ctx.LocalLlmConfig.Model = input;
        ctx.Agent.LocalLlmModel = input;
        ctx.Agent.UpdateLocalLlmConfig(ctx.LocalLlmConfig);
        Console.WriteLine($"  ✅ Модель изменена на: {input}");
        Console.WriteLine();
    }

    private async Task CheckAsync(CommandContext ctx)
    {
        Console.WriteLine("  🔍 Проверка подключения к Ollama...");
        var client = await EnsureClientAsync(ctx);
        var available = await client.IsAvailableAsync();

        if (available)
        {
            Console.WriteLine("  ✅ Ollama доступен!");
            var models = await client.ListModelsAsync();
            if (models.Any())
            {
                Console.WriteLine($"  📦 Загруженные модели ({models.Count}):");
                foreach (var m in models)
                {
                    var marker = m == ctx.LocalLlmConfig.Model ? " ▶ (текущая)" : "";
                    Console.WriteLine($"     • {m}{marker}");
                }
            }
            else
            {
                Console.WriteLine("  ⚠️  Модели не найдены. Загрузите: ollama pull llama3.1");
            }
        }
        else
        {
            Console.WriteLine($"  ❌ Ollama недоступен по {ctx.LocalLlmConfig.Url}");
            Console.WriteLine("     Запустите: ollama serve");
        }
        Console.WriteLine();
    }

    private async Task ListModelsAsync(CommandContext ctx)
    {
        Console.WriteLine("  📦 Загруженные модели Ollama:");
        var client = await EnsureClientAsync(ctx);
        var models = await client.ListModelsAsync();

        if (models.Any())
        {
            foreach (var m in models)
            {
                var marker = m == ctx.LocalLlmConfig.Model ? " ▶ (текущая)" : "";
                Console.WriteLine($"     • {m}{marker}");
            }
        }
        else
        {
            Console.WriteLine("     (пусто — запустите 'ollama pull <model>')");
        }
        Console.WriteLine();
    }

    private async Task SetUrlAsync(CommandContext ctx, string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("  ⚠️  Укажите URL: /local url http://localhost:11434");
            Console.WriteLine();
            return;
        }
        ctx.LocalLlmConfig.Url = parts[1];
        ctx.Agent.LocalLlmUrl = parts[1];
        ctx.Agent.UpdateLocalLlmConfig(ctx.LocalLlmConfig);
        Console.WriteLine($"  ✅ URL изменён на: {parts[1]}");
        Console.WriteLine();
    }

    private async Task SetTempAsync(CommandContext ctx, string[] parts)
    {
        if (parts.Length < 2 || !double.TryParse(parts[1], out var temp))
        {
            Console.WriteLine("  ⚠️  Укажите число: /local temp 0.7");
            Console.WriteLine();
            return;
        }
        ctx.LocalLlmConfig.Temperature = temp;
        ctx.Agent.LocalLlmTemperature = temp;
        ctx.Agent.UpdateLocalLlmConfig(ctx.LocalLlmConfig);
        Console.WriteLine($"  ✅ Temperature установлен в {temp}");
        Console.WriteLine();
    }

    private async Task SetMaxTokensAsync(CommandContext ctx, string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var tokens))
        {
            Console.WriteLine("  ⚠️  Укажите число: /local maxtokens 4096");
            Console.WriteLine();
            return;
        }
        ctx.LocalLlmConfig.MaxTokens = tokens;
        ctx.Agent.LocalLlmMaxTokens = tokens;
        ctx.Agent.UpdateLocalLlmConfig(ctx.LocalLlmConfig);
        Console.WriteLine($"  ✅ MaxTokens установлен в {tokens}");
        Console.WriteLine();
    }

    private static Task PrintUsageAsync()
    {
        Console.WriteLine("  Использование:");
        Console.WriteLine("    /local status              — показать статус");
        Console.WriteLine("    /local on                  — включить локальную LLM");
        Console.WriteLine("    /local off                 — выключить (fallback на GigaChat)");
        Console.WriteLine("    /local toggle              — переключить вкл/выкл");
        Console.WriteLine("    /local model               — выбрать модель");
        Console.WriteLine("    /local check               — проверить подключение");
        Console.WriteLine("    /local models              — список моделей");
        Console.WriteLine("    /local url <адрес>         — изменить URL Ollama");
        Console.WriteLine("    /local temp <0-2>          — температура");
        Console.WriteLine("    /local maxtokens <число>   — макс. токенов");
        Console.WriteLine();
        return Task.CompletedTask;
    }
}
