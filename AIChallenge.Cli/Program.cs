using AIChallenge.Core.Services;
using AIChallenge.Core.Infrastructure;
using AIChallenge.Services;
using AIChallenge.Core;
using AIChallenge.Cli.Commands;
using AIChallenge.Models;

using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("═══════════════════════════════════════════════");
Console.WriteLine("   GigaChat CLI Client");
Console.WriteLine("   Общение с LLM через GigaChat API");
Console.WriteLine("═══════════════════════════════════════════════");
Console.WriteLine();

var builder = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

var configuration = builder.Build();
var config = new GigaChatConfig();
configuration.GetSection("GigaChat").Bind(config);

// Load ContextConfig manually (Binder not available in Models project)
var ctxOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var ctxSection = configuration.GetSection("Context");
config.Context = JsonSerializer.Deserialize<ContextConfig>(ctxSection.Value!, ctxOptions) ?? new ContextConfig();
config.Context.SlidingWindow = ctxSection.GetSection("SlidingWindow").Get<SlidingWindowConfig>() ?? new SlidingWindowConfig();
config.Context.StickyFacts = ctxSection.GetSection("StickyFacts").Get<StickyFactsConfig>() ?? new StickyFactsConfig();
config.Context.Branching = ctxSection.GetSection("Branching").Get<BranchingConfig>() ?? new BranchingConfig();
config.Context.Summary = ctxSection.GetSection("Summary").Get<SummaryConfig>() ?? new SummaryConfig();

if (string.IsNullOrEmpty(config.ClientId))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("❌ Ошибка: не загружена конфигурация.");
    Console.WriteLine("   Проверьте файл appsettings.json.");
    Console.WriteLine();
    Console.WriteLine("   1. Зарегистрируйтесь: https://developer.sber.ru/portal/dev/products/gigachat");
    Console.WriteLine("   2. Создайте приложение и получите Client ID и Client Secret");
    Console.WriteLine("   3. Внесите их в appsettings.json");
    Console.ResetColor();
    return;
}

Console.WriteLine("✅ Конфигурация загружена");
Console.WriteLine();

// Выбор модели
Console.WriteLine("📦 Доступные модели:");
foreach (var kvp in AvailableModels.Models)
{
    var marker = kvp.Value == config.Model ? " ▶" : "";
    Console.WriteLine($"   {kvp.Key}. {kvp.Value}{marker}");
}
Console.WriteLine();
Console.WriteLine("   Для смены модели во время работы используйте команду /model");
Console.WriteLine();
Console.WriteLine();
Console.WriteLine("📖 Введите /help для списка команд.");
Console.WriteLine();

var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
};

using var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri(ApiEndpoints.BaseAddress),
    Timeout = TimeSpan.FromMinutes(5),
};

var authClient = new AuthClient(httpClient, config);
var chatClient = new ChatClient(httpClient);
var logger = new AgentLogger(LogLevel.Info);
var cache = new RequestCache(maxSize: 100);
var memoryManager = new MemoryManager(logger);

// Применяем конфигурацию памяти из appsettings.json
var memoryConfig = new MemoryConfig();
configuration.GetSection("Memory").Bind(memoryConfig);

memoryManager.ShortTerm.MaxSize = memoryConfig.ShortTerm.MaxSize;
memoryManager.ShortTerm.DecayEnabled = memoryConfig.ShortTerm.DecayEnabled;
memoryManager.ShortTerm.DecayAfterHours = memoryConfig.ShortTerm.DecayAfterHours;
memoryManager.Working.ArchiveEnabled = memoryConfig.Working.ArchiveEnabled;
memoryManager.Working.MaxArchiveSize = memoryConfig.Working.MaxArchiveSize;
memoryManager.LongTerm.MaxSize = memoryConfig.LongTerm.MaxSize;

Console.WriteLine($"📦 Конфигурация памяти:");
Console.WriteLine($"   Краткосрочная: max={memoryConfig.ShortTerm.MaxSize}, decay={(memoryConfig.ShortTerm.DecayEnabled ? "вкл" : "выкл")}");
Console.WriteLine($"   Рабочая: archive={(memoryConfig.Working.ArchiveEnabled ? "вкл" : "выкл")}, max_archive={memoryConfig.Working.MaxArchiveSize}");
Console.WriteLine($"   Долгосрочная: max={memoryConfig.LongTerm.MaxSize}");
Console.WriteLine();

// Инициализация управления контекстом
var contextConfig = new ContextManagerConfig
{
    Enabled = config.Context.Enabled,
    RecentMessageCount = config.Context.SlidingWindow.WindowSize,
    SummaryInterval = config.Context.Summary.Interval,
    MaxSummaries = config.Context.Summary.MaxSummaries,
    MaxContextTokens = config.Context.Summary.MaxContextTokens,
};

var contextManager = new ContextManager(
    chatClient,
    authClient,
    logger,
    contextConfig,
    config.Context.SlidingWindow,
    config.Context.StickyFacts,
    config.Context.Branching
);
var agent = new ChatAgent(chatClient, authClient, cache, logger, memoryManager, null, null, contextManager);
var adaptive = new AdaptiveBehavior(agent.Metrics, cache, logger);
var planner = new Planner(chatClient, authClient, config.Model, logger, memoryManager);
agent.Adaptive = adaptive;
agent.Planner = planner;
agent.Config = config;

// Загрузка профиля агента — всегда из profiles/agent_profile.json
agent.AgentProfile = AgentProfileManager.Load("default");
Console.WriteLine($"🤖 Профиль агента загружен: {agent.AgentProfile.Name} (стиль: {agent.AgentProfile.Style}, формат: {agent.AgentProfile.Format}, глубина: {agent.AgentProfile.Depth})");

// API-параметры — из конфига (не относятся к профилю агента)
agent.Model = config.Model;
agent.Temperature = config.Temperature;
agent.StopSequences = config.StopSequences;

// SystemMessage формируется из профиля агента — не берём из конфига
agent.SystemMessage = string.Empty;

agent.Metrics.ContextCompressionEnabled = contextConfig.Enabled;

// Применяем стратегию из config
if (Enum.TryParse(config.Context.Strategy, ignoreCase: true, out ContextStrategy loadedStrategy))
{
    agent.ContextManager.SetStrategy(loadedStrategy);
    agent.ContextManager.Config.Strategy = loadedStrategy;
    Console.WriteLine($"✅ Стратегия контекста: {loadedStrategy}");

    // Выводим настройки активной стратегии
    switch (loadedStrategy)
    {
        case ContextStrategy.StickyFacts when agent.ContextManager.StickyFacts is { } sf:
            Console.WriteLine($"   StickyFacts: window={sf.WindowSize}, maxFacts={sf.MaxFacts}");
            break;

        case ContextStrategy.Branching:
            Console.WriteLine($"   Branching: maxBranches={config.Context.Branching.MaxBranches}, maxCheckpoints={config.Context.Branching.MaxCheckpoints}");
            break;

        case ContextStrategy.SlidingWindow when agent.ContextManager.SlidingWindow is { } sw:
            Console.WriteLine($"   SlidingWindow: window={sw.WindowSize}");
            break;
    }
}
else
{
    Console.WriteLine($"⚠️  Неизвестная стратегия в config: '{config.Context.Strategy}', используем SlidingWindow");
    agent.ContextManager.SetStrategy(ContextStrategy.SlidingWindow);
    agent.ContextManager.Config.Strategy = ContextStrategy.SlidingWindow;
}

// Инициализация TaskStateMachine с подключением к GigaChat API
var taskStateMachine = new TaskStateMachine(chatClient, authClient, config, agent.AgentProfile);
Console.WriteLine("📋 TaskStateMachine инициализирован (с подключением к GigaChat API)");
Console.WriteLine("   Автозапуск: введите 'спроектируй', 'составь', 'разработай' и т.п.");
Console.WriteLine("   Вопросы задаются по одному. Введите /skip чтобы пропустить вопросы.");
Console.WriteLine("   Команды: /fsm status, /fsm questions, /fsm answer, /fsm next,");
Console.WriteLine("   /fsm pause, /fsm resume, /fsm transition, /fsm can, /fsm allowed,");
Console.WriteLine("   /fsm stages, /fsm plan, /fsm execute, /fsm validate, /fsm save,");
Console.WriteLine("   /fsm load, /fsm history, /fsm dialog, /fsm help");
Console.WriteLine();

// Регистируем команды
var ctx = new CommandContext(agent, config, taskStateMachine, logger, cache, memoryManager);

// Автоматическая регистрация всех CommandHandler из сборки
var assembly = typeof(CommandHandler).Assembly;
foreach (var type in assembly.GetTypes()
    .Where(t => typeof(CommandHandler).IsAssignableFrom(t) && !t.IsAbstract))
{
    CommandRegistry.Register((CommandHandler)Activator.CreateInstance(type)!);
}

Console.WriteLine("✅ Команды зарегистрированы");
Console.WriteLine();

// Инициализация MCP GitHub и реестра инструментов
McpGitHubService? mcpGitHubService = null;
McpTodoService? mcpTodoService = null;
var mcpRegistry = new McpToolRegistry();

try
{
    var mcpEnabled = configuration.GetSection("Mcp").GetValue<bool>("Enabled", false);
    if (mcpEnabled)
    {
        // Создаём делегат логирования, переиспользуя существующий logger
        void Log(string message, LogLevel level)
        {
            var color = level switch
            {
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Info => ConsoleColor.Green,
                _ => ConsoleColor.Gray
            };
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        mcpGitHubService = new McpGitHubService(Log, configuration);
        Console.WriteLine("🔌 MCP GitHub инициализирован");

        // Находим McpCommand и устанавливаем сервис
        var mcpCmd = CommandRegistry.Commands
            .Select(CommandRegistry.GetHandler)
            .OfType<McpCommand>()
            .FirstOrDefault();

        if (mcpCmd is not null)
        {
            mcpCmd.McpService = mcpGitHubService;
            Console.WriteLine("✅ McpCommand подключён к McpGitHubService");
        }

        // Инициализация TodoMCP
        try
        {
            var todoSection = configuration.GetSection("Mcp:Servers:Todo");
            if (todoSection.Exists())
            {
                mcpTodoService = new McpTodoService(Log, configuration);
                Console.WriteLine("📋 TodoMCP инициализирован");

                var todoCmd = CommandRegistry.Commands
                    .Select(CommandRegistry.GetHandler)
                    .OfType<McpTodoCommand>()
                    .FirstOrDefault();

                if (todoCmd is not null)
                {
                    todoCmd.TodoService = mcpTodoService;
                    Console.WriteLine("✅ McpTodoCommand подключён к McpTodoService");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Ошибка инициализации TodoMCP: {ex.Message}");
        }

        // Регистрируем инструменты из MCP-сервисов
        try
        {
            // GitHub MCP tools
            if (mcpGitHubService is not null)
            {
                // Сначала подключаем GitHub MCP
                Console.WriteLine("🔌 Подключение к GitHub MCP для регистрации инструментов...");
                try
                {
                    await mcpGitHubService.ConnectAsync();
                    foreach (var tool in mcpGitHubService.Tools)
                    {
                        var serviceName = "github";
                        mcpRegistry.Register(new McpToolRegistry.McpToolDefinition(
                            Name: tool.Name,
                            Description: tool.Description,
                            ServiceName: serviceName,
                            Call: async (args) =>
                            {
                                var dict = string.IsNullOrWhiteSpace(args)
                                    ? null
                                    : JsonSerializer.Deserialize<Dictionary<string, object?>>(args);
                                return await mcpGitHubService.CallToolAsync(tool.Name, dict);
                            }
                        ));
                    }
                    Console.WriteLine($"  ✅ GitHub: зарегистрировано {mcpGitHubService.Tools.Count} инструментов");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️  GitHub MCP не подключён: {ex.Message}");
                }
            }

            // TodoMCP tools
            if (mcpTodoService is not null)
            {
                // Сначала подключаем TodoMCP
                Console.WriteLine("📋 Подключение к TodoMCP для регистрации инструментов...");
                try
                {
                    await mcpTodoService.ConnectAsync();
                    foreach (var tool in mcpTodoService.Tools)
                    {
                        mcpRegistry.Register(new McpToolRegistry.McpToolDefinition(
                            Name: tool.Name,
                            Description: tool.Description,
                            ServiceName: "todo",
                            InputSchema: tool.InputSchema,
                            Call: async (args) =>
                            {
                                var dict = string.IsNullOrWhiteSpace(args)
                                    ? null
                                    : JsonSerializer.Deserialize<Dictionary<string, object?>>(args);
                                return await mcpTodoService.CallToolAsync(tool.Name, dict);
                            }
                        ));
                    }
                    Console.WriteLine($"  ✅ TodoMCP: зарегистрировано {mcpTodoService.Tools.Count} инструментов");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️  TodoMCP не подключён: {ex.Message}");
                }
            }

            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Ошибка регистрации MCP-инструментов: {ex.Message}");
        }
        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️  Ошибка инициализации MCP: {ex.Message}");
    Console.WriteLine();
}

// Загружаем контекст из предыдущей сессии
ContextPersistence.LoadContext(agent);

// Подключаем MCP-реестр к агенту
if (mcpRegistry.Tools.Count > 0)
{
    agent.McpRegistry = mcpRegistry;
    Console.WriteLine($"🔧 MCP-реестр: {mcpRegistry.Tools.Count} инструментов доступно");
}
Console.WriteLine();

Console.WriteLine("🔄 Инициализация подключения к GigaChat...");

try
{
    var token = await authClient.GetAccessTokenAsync();
    Console.WriteLine("✅ Подключение успешно!");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"❌ Ошибка аутентификации: {ex.Message}");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("   Проверьте ваши учётные данные в appsettings.json.");
    return;
}

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("✏️  Вы: ");
    Console.ResetColor();

    var input = Console.ReadLine()?.Trim();

    if (input is null or "quit" or "exit" or "выход" or "q")
    {
        // Сохраняем контекст перед выходом
        ContextPersistence.SaveContext(agent);

        // Сохраняем профиль агента
        AgentProfileManager.Save(agent.AgentProfile);
        Console.WriteLine($"💾 Профиль агента сохранён: {agent.AgentProfile.Name}");

        // Освобождаем MCP-ресурсы
        if (mcpGitHubService is not null)
        {
            await mcpGitHubService.DisposeAsync();
            Console.WriteLine("🔌 MCP GitHub-соединение закрыто");
        }

        if (mcpTodoService is not null)
        {
            await mcpTodoService.DisposeAsync();
            Console.WriteLine("📋 TodoMCP-соединение закрыто");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("👋 До свидания!");
        Console.ResetColor();

        // Итоговая статистика по токенам (реальные данные из API)
        var met = agent.Metrics;
        var totalTokens = met.TotalPromptTokens + met.TotalCompletionTokens;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"📊 Токены: {met.TotalPromptTokens} в → {met.TotalCompletionTokens} out → {totalTokens} всего");
        Console.ResetColor();

        break;
    }

    // Команды конфигурации
    if (input.StartsWith("/"))
    {
        var parts = input.Split(' ', 4, StringSplitOptions.TrimEntries);
        var command = parts[0].TrimStart('/').ToLowerInvariant();

        var cmdHandler = CommandRegistry.GetHandler(command);
        if (cmdHandler is not null)
        {
            await cmdHandler.ExecuteAsync(parts, ctx);
            continue;
        }
    }

    if (string.IsNullOrEmpty(input))
    {
        continue;
    }

    // === Автоматический запуск FSM при ключевых словах ===
    // FSM НЕ запускается, если доступны MCP-инструменты — пользователь может хотеть
    // создать/обновить задачу через MCP, а не запустить FSM-сбор требований
    var lowerInput = input.ToLowerInvariant();
    var isFsmTrigger = FsmKeywords.All.Any(kw => lowerInput.StartsWith(kw + " ") || lowerInput.StartsWith(kw + "，") || lowerInput == kw);

    var reqStatus = taskStateMachine.GetRequirementsStatus();
    var shouldHandleFsm = taskStateMachine.RequirementsContext is not null && reqStatus is not null && !reqStatus.IsComplete;

    if (shouldHandleFsm || (isFsmTrigger && agent.McpRegistry is null))
    {
        await FsmHandler.HandleFsmInputAsync(input, taskStateMachine, config);
        continue;
    }

    // === Обычный запрос ===
    await FsmHandler.HandleNormalInputAsync(input, agent);
}

// Graceful shutdown: save context on Ctrl+C
Console.CancelKeyPress += (s, e) =>
{
    ContextPersistence.SaveContext(agent);
    AgentProfileManager.Save(agent.AgentProfile);
    Console.WriteLine();
    Console.WriteLine("💾 Сохранение...");
    e.Cancel = true;
    Environment.Exit(0);
};
