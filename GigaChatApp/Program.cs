using GigaChatApp.Models;
using GigaChatApp.Services;
using GigaChatApp.Infrastructure;
using GigaChatApp;

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
config.Context = ContextConfig.Load(configuration.GetSection("Context"));

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
Console.WriteLine("📖 Команды: /status, /model, /system <текст>, /maxtokens <число>, /stop <seq1,seq2>, /temp <0-2>");
Console.WriteLine("   Адаптация: /adaptive (статус), /adaptive on/off/reset/threshold <значение>");
Console.WriteLine("   Планировщик: /planner (статус)");
Console.WriteLine("   Память: /memory list, /memory save <ключ> <значение>, /memory delete <ключ>, /memory search <запрос>, /memory status, /memory extract");
Console.WriteLine("   Контекст: /context (статус), /context strategy (список), /context strategy <sliding|sticky|branching>");
Console.WriteLine("   Факты: /facts list, /facts save <ключ> <значение>, /facts delete <ключ>");
Console.WriteLine("   Ветки: /branch list, /branch create <имя>, /branch switch <id>, /branch checkpoint <имя>, /branch create-from <cp-id> <имя>, /branch delete <id>");
Console.WriteLine("   Профиль: /agent-profile (статус), /agent-profile name/role/style/format/language/depth/domain, /agent-profile tech add/remove/list/clear, /agent-profile constraint/req/instructions/reset");
Console.WriteLine("   Очистка: /clear | Сохранить: /save | Выход: quit / exit / q");
Console.WriteLine("   По умолчанию ограничений нет — задайте через команды выше.");
Console.WriteLine();

var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
};

using var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri("https://api.giga.chat"),
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

// Применяем стратегию из конфига
if (Enum.TryParse(config.Context.Strategy, ignoreCase: true, out ContextStrategy strategy))
{
    contextConfig.Strategy = strategy;
    Console.WriteLine($"📦 Стратегия контекста: {strategy}");
}
else
{
    Console.WriteLine($"❌ Неизвестная стратегия: '{config.Context.Strategy}', используем SlidingWindow");
    contextConfig.Strategy = ContextStrategy.SlidingWindow;
}

// Настройки стратегии StickyFacts
int stickyFactsWindowSize = config.Context.StickyFacts.WindowSize;
int stickyFactsMaxFacts = config.Context.StickyFacts.MaxFacts;

// Настройки Branching
int maxBranches = config.Context.Branching.MaxBranches;
int maxCheckpoints = config.Context.Branching.MaxCheckpoints;

var contextManager = new ContextManager(
    chatClient,
    authClient,
    logger,
    contextConfig,
    stickyFactsWindowSize,
    stickyFactsMaxFacts
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

// Загружаем стратегию из config
if (Enum.TryParse(config.Context.Strategy, ignoreCase: true, out ContextStrategy loadedStrategy))
{
    agent.ContextManager.SetStrategy(loadedStrategy);
    agent.ContextManager.Config.Strategy = loadedStrategy;
    Console.WriteLine($"✅ Стратегия контекста: {loadedStrategy}");
}
else
{
    Console.WriteLine($"⚠️  Неизвестная стратегия в config: '{config.Context.Strategy}', используем SlidingWindow");
    agent.ContextManager.SetStrategy(ContextStrategy.SlidingWindow);
    agent.ContextManager.Config.Strategy = ContextStrategy.SlidingWindow;
}

// Применяем настройки стратегии StickyFacts
if (agent.ContextManager.StickyFacts is { } stickyFacts)
{
    // Обновляем window size через рефлексию или перезагрузку
    Console.WriteLine($"   StickyFacts: window={stickyFactsWindowSize}, maxFacts={stickyFactsMaxFacts}");
}

// Применяем настройки Branching
if (agent.ContextManager.Branching is { } branching)
{
    Console.WriteLine($"   Branching: maxBranches={maxBranches}, maxCheckpoints={maxCheckpoints}");
}
else
{
    Console.WriteLine($"⚠️  Неизвестная стратегия в config: '{config.ContextStrategy}', используем SlidingWindow");
    agent.ContextManager.SetStrategy(ContextStrategy.SlidingWindow);
    agent.ContextManager.Config.Strategy = ContextStrategy.SlidingWindow;
}

// Загружаем контекст из предыдущей сессии
ContextPersistence.LoadContext(agent);

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

    if (input is "clear" or "очисти" or "/clear")
    {
        agent.ClearHistory();
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("🗑  История очищена.");
        Console.ResetColor();
        Console.WriteLine();
        continue;
    }

    // Команды конфигурации
    if (input.StartsWith("/"))
    {
        var parts = input.Split(' ', 4, StringSplitOptions.TrimEntries);
        var command = parts[0].ToLowerInvariant();

        switch (command)
        {
            case "/status":
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("📋 Текущие настройки агента:");
                Console.ResetColor();
                Console.WriteLine($"   Модель: {agent.Model}");
                Console.WriteLine($"   Temperature: {agent.Temperature ?? (object)"(не задано)"}");
                Console.WriteLine($"   MaxTokens: {agent.MaxTokens}");
                Console.WriteLine($"   StopSequences: [{string.Join(", ", agent.StopSequences.Select(s => $"\"{s}\""))}]");
                Console.WriteLine($"   SystemMessage: {agent.SystemMessage}");
                Console.WriteLine();

                // Профиль агента
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("🤖 Профиль агента:");
                Console.ResetColor();
                var prof = agent.AgentProfile;
                Console.WriteLine($"   Имя: {prof.Name}");
                if (!string.IsNullOrWhiteSpace(prof.Role))
                    Console.WriteLine($"   Роль: {prof.Role}");
                Console.WriteLine($"   Стиль: {prof.Style} | Формат: {prof.Format} | Язык: {prof.Language}");
                Console.WriteLine($"   Глубина: {prof.Depth} | Домен: {(string.IsNullOrWhiteSpace(prof.Domain) ? "(не задан)" : prof.Domain)}");
                if (prof.PreferredTechnologies.Count > 0)
                    Console.WriteLine($"   Технологии: {string.Join(", ", prof.PreferredTechnologies)}");
                Console.WriteLine();

                // Статус контекста
                var cmStatus = agent.ContextManager;
                var cfgStatus = cmStatus.Config;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("📦 Управление контекстом:");
                Console.ResetColor();
                Console.WriteLine($"   Включено: {(cfgStatus.Enabled ? "да" : "нет")}");
                Console.WriteLine($"   Recent: {cfgStatus.RecentMessageCount}, Interval: {cfgStatus.SummaryInterval}");
                Console.WriteLine($"   Summary блоков: {cmStatus.SummaryCount}");
                Console.WriteLine();

                Console.WriteLine("📈 Метрики:");
                Console.ResetColor();
                var m = agent.Metrics;
                Console.WriteLine($"   Запросов: {m.TotalRequests}  |  Успешно: {m.SuccessfulRequests}  |  Ошибок: {m.FailedRequests}");
                Console.WriteLine($"   Из кэша: {m.CachedRequests}  |  Retry: {m.RetryAttempts}");
                Console.WriteLine($"   Success rate: {m.SuccessRate:P1}");
                Console.WriteLine($"   Avg duration: {m.TotalDuration.TotalMilliseconds:F0} мс");
                Console.WriteLine($"   Токены: {m.TotalPromptTokens} in → {m.TotalCompletionTokens} out");
                Console.WriteLine($"   Токены контекста: {m.TotalContextTokens} всего, {m.LastContextTokens} последний");
                Console.WriteLine($"   Кэш: {agent.Cache.Count} записей");
                Console.WriteLine();

                // Статус памяти
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("🧠 Память:");
                Console.ResetColor();
                Console.WriteLine($"   Краткосрочная (диалог): {agent.MemoryManager.ShortTerm.Count} записей");
                Console.WriteLine($"   Рабочая (задача):       {agent.MemoryManager.Working.Count} записей, задача: {agent.MemoryManager.Working.CurrentTaskId ?? "нет"}");
                Console.WriteLine($"   Долгосрочная (знания):  {agent.MemoryManager.LongTerm.Count} фактов");
                Console.WriteLine();
                continue;

            case "/model":
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("📦 Доступные модели:");
                Console.ResetColor();
                foreach (var kvp in AvailableModels.Models)
                {
                    var marker = kvp.Value == agent.Model ? " ▶" : "";
                    Console.WriteLine($"   {kvp.Key}. {kvp.Value}{marker}");
                }
                Console.WriteLine();
                Console.Write("   Введите номер или название: ");
                Console.ResetColor();

                var modelInputCmd = Console.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(modelInputCmd))
                {
                    if (int.TryParse(modelInputCmd, out var modelNumber) && AvailableModels.Models.ContainsKey(modelNumber))
                    {
                        agent.Model = AvailableModels.Models[modelNumber];
                        config.Model = agent.Model;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Модель изменена на: {agent.Model}");
                        Console.ResetColor();
                    }
                    else if (AvailableModels.Models.ContainsValue(modelInputCmd))
                    {
                        agent.Model = modelInputCmd;
                        config.Model = agent.Model;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Модель изменена на: {agent.Model}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Модель '{modelInputCmd}' не найдена.");
                        Console.ResetColor();
                    }
                }
                Console.WriteLine();
                continue;

            case "/system":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите новое системное сообщение: /system <текст>");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                agent.SystemMessage = parts[1];
                config.SystemMessage = parts[1];
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ SystemMessage обновлён.");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            case "/maxtokens":
                if (parts.Length < 2 || !int.TryParse(parts[1], out var maxTokens))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите число: /maxtokens <число>");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                agent.MaxTokens = maxTokens;
                config.MaxTokens = maxTokens;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ MaxTokens установлен в {maxTokens}.");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            case "/stop":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите стоп-последовательности через запятую: /stop <seq1,seq2,...>");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                agent.StopSequences = parts[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                config.StopSequences = agent.StopSequences;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ StopSequences обновлены: [{string.Join(", ", agent.StopSequences.Select(s => $"\"{s}\""))}]");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            case "/temp":
            case "/temperature":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите число: /temp <0-2>. Пример: /temp 0.7");
                    Console.WriteLine("   /temp clear — сбросить ограничение.");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                if (parts[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    agent.Temperature = null;
                    config.Temperature = null;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✅ Temperature сброшен.");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                double temp;
                if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out temp))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите число: /temp <0-2>. Пример: /temp 0.7");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                if (temp < 0 || temp > 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Temperature должно быть в диапазоне 0–2.");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                agent.Temperature = temp;
                config.Temperature = temp;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ Temperature установлен в {temp}.");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            case "/retry":
                if (parts.Length < 2 || !int.TryParse(parts[1], out var retryCount))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите число: /retry <count>");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                agent.Metrics.RetryCount = retryCount;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ RetryCount установлен в {retryCount}.");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            case "/retrydelay":
                if (parts.Length < 2 || !int.TryParse(parts[1], out var retryDelay))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите число: /retrydelay <ms>");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                agent.Metrics.RetryDelayMs = retryDelay;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ RetryDelayMs установлен в {retryDelay}.");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            case "/loglevel":
                if (parts.Length < 2 || !Enum.TryParse(parts[1], ignoreCase: true, out LogLevel logLevel))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите уровень: /loglevel [debug|info|warning|error]");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                agent.Logger.SetMinLevel(logLevel);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ Уровень логирования: {logLevel}");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            case "/metrics":
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("📊 Метрики агента:");
                Console.ResetColor();
                var met = agent.Metrics;
                Console.WriteLine($"   Запросов: {met.TotalRequests}");
                Console.WriteLine($"   Успешно: {met.SuccessfulRequests}");
                Console.WriteLine($"   Ошибок: {met.FailedRequests}");
                Console.WriteLine($"   Из кэша: {met.CachedRequests}");
                Console.WriteLine($"   Retry: {met.RetryAttempts}");
                Console.WriteLine($"   Success rate: {met.SuccessRate:P1}");
                Console.WriteLine($"   Avg duration: {met.TotalDuration.TotalMilliseconds:F0} мс");
                Console.WriteLine($"   Токены: {met.TotalPromptTokens} in → {met.TotalCompletionTokens} out");
                Console.WriteLine($"   Токены контекста: {met.TotalContextTokens} всего, {met.LastContextTokens} последний");
                Console.WriteLine($"   Кэш: {agent.Cache.Count} записей");
                Console.WriteLine($"   Память: {agent.MemoryManager.ShortTerm.Count} кратк. | {agent.MemoryManager.Working.Count} рабоч. | {agent.Memory.Count} длг.");
                Console.WriteLine();

                // Метрики контекста
                if (met.ContextComparison.ComparisonCount > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("📦 Метрики управления контекстом:");
                    Console.ResetColor();
                    Console.WriteLine(met.ContextComparison.GetReport());
                }
                continue;

            case "/adaptive":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("🔄 Адаптивное поведение:");
                    Console.ResetColor();
                    Console.WriteLine(agent.Adaptive.GetStatus());
                    Console.WriteLine("   Команды: /adaptive on, /adaptive off, /adaptive reset, /adaptive threshold <0-1>");
                    Console.WriteLine();
                    continue;
                }

                var adaptiveCmd = parts[1].ToLowerInvariant();
                switch (adaptiveCmd)
                {
                    case "on":
                        agent.Adaptive.Enabled = true;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✅ Адаптация включена");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "off":
                        agent.Adaptive.Enabled = false;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("⚠️  Адаптация выключена");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "reset":
                        agent.Adaptive.Reset();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✅ Параметры сброшены к значениям по умолчанию");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "threshold":
                        if (parts.Length < 3 || !double.TryParse(parts[2], out var threshold))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /adaptive threshold <0-1>. Пример: /adaptive threshold 0.2");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (threshold < 0 || threshold > 1)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Threshold должен быть в диапазоне 0–1");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        agent.Adaptive.FailureThreshold = threshold;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Порог ошибок установлен: {threshold:P0}");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Неизвестная команда адаптации: {adaptiveCmd}. Доступны: on, off, reset, threshold");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;
                }
                continue;

            case "/planner":
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("📋 Планировщик:");
                Console.ResetColor();
                Console.WriteLine($"   Включён: {(agent.Planner != null ? "да" : "нет")}");
                Console.WriteLine($"   MinComplexityLength: {agent.Planner?.MinComplexityLength}");
                Console.WriteLine($"   MaxTasks: {agent.Planner?.MaxTasks}");
                Console.WriteLine();
                Console.WriteLine("   Запросы длиннее MinComplexityLength и содержащие ключевые слова");
                Console.WriteLine("   автоматически разбиваются на подзадачи.");
                Console.WriteLine("   Ключевые слова: анализируй, сравни, перечисли, составь, создай,");
                Console.WriteLine("   разработай, изучи, каждый, все, какие, почему, как, объясни,");
                Console.WriteLine("   разбей, декомпозируй, пошагово, по шагам, последовательно");
                Console.WriteLine();
                continue;

            case "/plan":
                {
                    if (agent.History.Count < 1)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("❌ Нет сообщений в истории для планирования");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;
                    }

                    // Берём последнее сообщение пользователя
                    var lastUserMsg = agent.History
                        .Where(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                        .LastOrDefault();

                    if (lastUserMsg is null)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("❌ Нет сообщений пользователя в истории");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;
                    }

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"📋 Ручной запуск планировщика для: \"{lastUserMsg.Content[..Math.Min(60, lastUserMsg.Content.Length)]}\"");
                    Console.ResetColor();

                    // Показываем текущее состояние рабочей памяти
                    Console.WriteLine($"   До: рабочая память содержит {agent.MemoryManager.Working.Count} записей");

                    // Запускаем планировщик напрямую
                    var facts = agent.MemoryManager.LongTerm.FindRelevant(lastUserMsg.Content);
                    Console.WriteLine($"   Найдено {facts.Count} фактов для контекста");

                    var plan = await agent.Planner!.CreatePlanAsync(lastUserMsg!.Content, facts);
                    Console.WriteLine($"   CreatePlanAsync вернул: {(plan == null ? "null" : $"{plan.Tasks.Count} задач")}");

                    if (plan is null || plan.Tasks.Count == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("   LLM не создал план (запрос слишком простой)");
                        Console.ResetColor();

                        // Даже если план не создан — сохраняем запрос в рабочую память
                        agent.MemoryManager.Working.Save("request", lastUserMsg.Content, "request");
                        Console.WriteLine($"   Запрос сохранён в рабочую память. Всего записей: {agent.MemoryManager.Working.Count}");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"   ✅ План создан: {plan.Tasks.Count} подзадач");
                        Console.ResetColor();
                        foreach (var task in plan.Tasks)
                        {
                            Console.WriteLine($"     {task.Id}. {task.Description}");
                        }

                        // Сохраняем план в рабочую память
                        agent.MemoryManager.Working.SavePlan(plan);
                        agent.MemoryManager.Working.Save("request", lastUserMsg.Content, "request");
                        Console.WriteLine();
                        Console.WriteLine($"   План сохранён в рабочую память. Всего записей: {agent.MemoryManager.Working.Count}");
                        Console.WriteLine($"   Текущая задача: {agent.MemoryManager.Working.CurrentTaskId}");
                    }
                    Console.WriteLine();
                    break;
                }

            case "/memory":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Команды памяти: /memory list, /memory save <ключ> <значение>, /memory delete <ключ>, /memory search <запрос>, /memory status, /memory extract");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }

                var memoryCommand = parts[1].ToLowerInvariant();

                switch (memoryCommand)
                {
                    case "list":
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("🧠 Память агента:");
                        Console.ResetColor();
                        if (agent.Memory.Count == 0)
                        {
                            Console.WriteLine("   (пусто)");
                        }
                        else
                        {
                            foreach (var kvp in agent.Memory.All)
                            {
                                var fact = kvp.Value;
                                var color = fact.Source switch
                                {
                                    "user" => ConsoleColor.Green,
                                    "extracted" => ConsoleColor.Cyan,
                                    "explicit" => ConsoleColor.Magenta,
                                    _ => ConsoleColor.White,
                                };

                                Console.ForegroundColor = color;
                                Console.Write($"   [{fact.Source,-9}] ");
                                Console.ResetColor();
                                Console.Write($"{fact.Key}: ");
                                Console.ForegroundColor = ConsoleColor.Gray;
                                Console.WriteLine(fact.Value);
                                Console.ResetColor();
                            }
                        }
                        Console.WriteLine();
                        Console.WriteLine($"   Всего фактов: {agent.Memory.Count}");
                        Console.WriteLine();
                        break;

                    case "save":
                        if (parts.Length < 4)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /memory save <ключ> <значение>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        var saveKey = parts[2];
                        var saveValue = parts[3];
                        agent.Memory.Save(saveKey, saveValue, "explicit");
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Факт сохранён: {saveKey} = \"{saveValue}\"");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "delete":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /memory delete <ключ>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        var deleteKey = parts[2];
                        if (agent.Memory.Delete(deleteKey))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"✅ Факт удалён: {deleteKey}");
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"❌ Факт не найден: {deleteKey}");
                            Console.ResetColor();
                        }
                        Console.WriteLine();
                        break;

                    case "search":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /memory search <запрос>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        var searchQuery = parts[2];
                        var searchResults = agent.MemoryManager.LongTerm.FindRelevant(searchQuery);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"🔍 Поиск по долгосрочной памяти: \"{searchQuery}\"");
                        Console.ResetColor();
                        if (searchResults.Count == 0)
                        {
                            Console.WriteLine("   Ничего не найдено");
                        }
                        else
                        {
                            foreach (var fact in searchResults)
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.Write($"   → {fact.Key}: ");
                                Console.ResetColor();
                                Console.WriteLine(fact.Value);
                            }
                        }
                        Console.WriteLine();
                        break;

                    case "status":
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine("🧠 Статус всех слоёв памяти:");
                        Console.ResetColor();
                        Console.WriteLine();

                        // Краткосрочная
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("── Краткосрочная память (диалог) ──");
                        Console.ResetColor();
                        Console.WriteLine($"   Записей: {agent.MemoryManager.ShortTerm.Count}");
                        Console.WriteLine($"   Лимит: {agent.MemoryManager.ShortTerm.MaxSize}");
                        if (agent.MemoryManager.ShortTerm.Count > 0)
                        {
                            var last = agent.MemoryManager.ShortTerm.Last;
                            if (last is not null)
                            {
                                Console.ForegroundColor = ConsoleColor.Gray;
                                Console.WriteLine($"   Последняя: [{last.Role}] {last.Content[..Math.Min(80, last.Content.Length)]}");
                                Console.ResetColor();
                            }
                        }
                        Console.WriteLine();

                        // Рабочая
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("── Рабочая память (текущая задача) ──");
                        Console.ResetColor();
                        Console.WriteLine($"   Записей: {agent.MemoryManager.Working.Count}");
                        Console.WriteLine($"   Задача: {agent.MemoryManager.Working.CurrentTaskId ?? "нет"}");
                        Console.WriteLine($"   Статус: {agent.MemoryManager.Working.CurrentTaskStatus ?? "нет"}");
                        if (agent.MemoryManager.Working.Count > 0)
                        {
                            var entries = agent.MemoryManager.Working.LoadCurrentTaskData();
                            foreach (var entry in entries.Take(5))
                            {
                                Console.ForegroundColor = ConsoleColor.Gray;
                                Console.Write($"   [{entry.Type,-8}] {entry.Key}: ");
                                Console.ResetColor();
                                Console.WriteLine(entry.Value[..Math.Min(60, entry.Value.Length)]);
                            }
                            if (entries.Count > 5)
                                Console.WriteLine($"   ... и ещё {entries.Count - 5} записей");
                        }
                        Console.WriteLine();

                        // Долгосрочная
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("── Долгосрочная память (знания) ──");
                        Console.ResetColor();
                        Console.WriteLine($"   Фактов: {agent.MemoryManager.LongTerm.Count}");
                        if (agent.MemoryManager.LongTerm.Count > 0)
                        {
                            foreach (var kvp in agent.MemoryManager.LongTerm.All.Take(5))
                            {
                                var fact = kvp.Value;
                                var color = fact.Source switch
                                {
                                    "user" => ConsoleColor.Green,
                                    "extracted" => ConsoleColor.Cyan,
                                    "explicit" => ConsoleColor.Magenta,
                                    _ => ConsoleColor.White,
                                };
                                Console.ForegroundColor = color;
                                Console.Write($"   [{fact.Source,-9}] ");
                                Console.ResetColor();
                                Console.Write($"{fact.Key}: ");
                                Console.ForegroundColor = ConsoleColor.Gray;
                                Console.WriteLine(fact.Value[..Math.Min(60, fact.Value.Length)]);
                                Console.ResetColor();
                            }
                            if (agent.MemoryManager.LongTerm.Count > 5)
                                Console.WriteLine($"   ... и ещё {agent.MemoryManager.LongTerm.Count - 5} фактов");
                        }
                        Console.WriteLine();
                        break;

                    case "extract":
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("🧠 Извлечение фактов из полного диалога...");
                            Console.ResetColor();

                            var dialogue = agent.MemoryManager.ShortTerm.GetAll();
                            if (dialogue.Count == 0)
                            {
                                Console.WriteLine("   Диалог пуст");
                                Console.WriteLine();
                                break;
                            }

                            var count = await agent.ExtractFactsExplicitAsync(dialogue);
                            Console.ForegroundColor = count > 0 ? ConsoleColor.Green : ConsoleColor.Yellow;
                            Console.WriteLine(count > 0
                                ? $"✅ Извлечено {count} факт(ов) в долгосрочную память"
                                : "ℹ️ Факты не найдены (LLM не определил значимых фактов)");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Неизвестная команда памяти: {memoryCommand}. Доступны: list, save, delete, search, status, extract");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;
                }
                continue;

            // === Команды краткосрочной памяти (диалог) ===
            case "/st":
            case "/shortterm":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("🗨 Краткосрочная память (текущий диалог):");
                    Console.ResetColor();
                    Console.WriteLine($"   Записей: {agent.MemoryManager.ShortTerm.Count}");
                    Console.WriteLine($"   Максимум: {agent.MemoryManager.ShortTerm.MaxSize}");
                    Console.WriteLine("   Команды: /st list, /st recent <N>, /st search <запрос>, /st clear");
                    Console.WriteLine();
                    continue;
                }

                var stCmd = parts[1].ToLowerInvariant();
                switch (stCmd)
                {
                    case "list":
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("🗨 Краткосрочная память:");
                            Console.ResetColor();
                            var entries = agent.MemoryManager.ShortTerm.GetAll();
                            if (entries.Count == 0)
                            {
                                Console.WriteLine("   (пусто)");
                            }
                            else
                            {
                                foreach (var entry in entries)
                                {
                                    var color = entry.Role switch
                                    {
                                        "user" => ConsoleColor.Green,
                                        "assistant" => ConsoleColor.Cyan,
                                        "system" => ConsoleColor.Gray,
                                        _ => ConsoleColor.White,
                                    };
                                    Console.ForegroundColor = color;
                                    Console.Write($"   [{entry.Role,-10}] ");
                                    Console.ResetColor();
                                    Console.WriteLine(entry.Content);
                                }
                            }
                            Console.WriteLine();
                            break;
                        }

                    case "recent":
                        {
                            int count = Math.Min(20, agent.MemoryManager.ShortTerm.Count);
                            if (parts.Length >= 3 && int.TryParse(parts[2], out var requested))
                            {
                                count = Math.Max(1, Math.Min(requested, agent.MemoryManager.ShortTerm.Count));
                            }
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"🗨 Последние {count} записей:");
                            Console.ResetColor();
                            var recent = agent.MemoryManager.ShortTerm.GetRecent(count);
                            foreach (var entry in recent)
                            {
                                Console.ForegroundColor = ConsoleColor.Gray;
                                Console.Write($"   [{entry.Role,-10}] ");
                                Console.ResetColor();
                                Console.WriteLine(entry.Content);
                            }
                            Console.WriteLine();
                            break;
                        }

                    case "search":
                        {
                            if (parts.Length < 3)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("❌ Формат: /st search <запрос>");
                                Console.ResetColor();
                                Console.WriteLine();
                                break;
                            }
                            var query = string.Join(" ", parts.Skip(2));
                            var results = agent.MemoryManager.ShortTerm.Search(query);
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"🔍 Поиск в краткосрочной памяти: \"{query}\"");
                            Console.ResetColor();
                            if (results.Count == 0)
                            {
                                Console.WriteLine("   Ничего не найдено");
                            }
                            else
                            {
                                foreach (var entry in results)
                                {
                                    Console.ForegroundColor = ConsoleColor.Cyan;
                                    Console.Write($"   → [{entry.Role,-10}] ");
                                    Console.ResetColor();
                                    Console.WriteLine(entry.Content);
                                }
                            }
                            Console.WriteLine();
                            break;
                        }

                    case "clear":
                        agent.MemoryManager.ClearShortTerm();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✅ Краткосрочная память очищена");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Неизвестная команда: {stCmd}. Доступны: list, recent, search, clear");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;
                }
                continue;

            // === Команды рабочей памяти (текущая задача) ===
            case "/wt":
            case "/working":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("⚙ Рабочая память (текущая задача):");
                    Console.ResetColor();
                    Console.WriteLine($"   Записей: {agent.MemoryManager.Working.Count}");
                    Console.WriteLine($"   Задача: {agent.MemoryManager.Working.CurrentTaskId ?? "нет"} [{agent.MemoryManager.Working.CurrentTaskStatus ?? "нет"}]");
                    Console.WriteLine("   Команды: /wt list, /wt save <ключ> <значение>, /wt delete <ключ>, /wt task <id>");
                    Console.WriteLine();
                    continue;
                }

                var wtCmd = parts[1].ToLowerInvariant();
                switch (wtCmd)
                {
                    case "list":
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("⚙ Рабочая память:");
                            Console.ResetColor();
                            var entries = agent.MemoryManager.Working.LoadCurrentTaskData();
                            if (entries.Count == 0)
                            {
                                Console.WriteLine("   (пусто)");
                            }
                            else
                            {
                                foreach (var entry in entries)
                                {
                                    var color = entry.Type switch
                                    {
                                        "plan" => ConsoleColor.Magenta,
                                        "task" => ConsoleColor.Cyan,
                                        "result" => ConsoleColor.Green,
                                        "fact" => ConsoleColor.Yellow,
                                        _ => ConsoleColor.White,
                                    };
                                    Console.ForegroundColor = color;
                                    Console.Write($"   [{entry.Type,-8}] ");
                                    Console.ResetColor();
                                    Console.Write($"{entry.Key}: ");
                                    Console.ForegroundColor = ConsoleColor.Gray;
                                    Console.WriteLine(entry.Value);
                                    Console.ResetColor();
                                }
                            }
                            Console.WriteLine();
                            break;
                        }

                    case "save":
                        {
                            if (parts.Length < 4)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("❌ Формат: /wt save <ключ> <значение>");
                                Console.ResetColor();
                                Console.WriteLine();
                                break;
                            }
                            var key = parts[2];
                            var value = parts[3];
                            agent.MemoryManager.Save(MemoryType.Working, key, value, "user");
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"✅ Сохранено в рабочую память: {key} = \"{value}\"");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }

                    case "delete":
                        {
                            if (parts.Length < 3)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("❌ Формат: /wt delete <ключ>");
                                Console.ResetColor();
                                Console.WriteLine();
                                break;
                            }
                            var key = parts[2];
                            if (agent.MemoryManager.Working.Delete(key))
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"✅ Удалено из рабочей памяти: {key}");
                                Console.ResetColor();
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"❌ Не найдено: {key}");
                                Console.ResetColor();
                            }
                            Console.WriteLine();
                            break;
                        }

                    case "task":
                        {
                            if (parts.Length < 3)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("❌ Формат: /wt task <start|complete|fail> [id]");
                                Console.ResetColor();
                                Console.WriteLine();
                                break;
                            }
                            var taskAction = parts[2];
                            switch (taskAction)
                            {
                                case "start":
                                    var taskId = parts.Length >= 4 ? parts[3] : Guid.NewGuid().ToString("N")[..8];
                                    agent.MemoryManager.StartTask(taskId);
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"✅ Начата задача: {taskId}");
                                    Console.ResetColor();
                                    break;

                                case "complete":
                                    var completeResult = parts.Length >= 4 ? string.Join(" ", parts.Skip(3)) : null;
                                    agent.MemoryManager.CompleteTask(completeResult);
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine("✅ Текущая задача завершена");
                                    Console.ResetColor();
                                    break;

                                case "fail":
                                    var reason = parts.Length >= 4 ? string.Join(" ", parts.Skip(3)) : "не указано";
                                    agent.MemoryManager.FailTask(reason);
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"❌ Текущая задача провалена: {reason}");
                                    Console.ResetColor();
                                    break;

                                default:
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"❌ Неизвестное действие: {taskAction}. Доступны: start, complete, fail");
                                    Console.ResetColor();
                                    break;
                            }
                            Console.WriteLine();
                            break;
                        }

                    case "clear":
                        agent.MemoryManager.ClearWorking();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✅ Рабочая память очищена");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Неизвестная команда: {wtCmd}. Доступны: list, save, delete, task, clear");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;
                }
                continue;

            case "/context":
                if (parts.Length < 2)
                {
                    // Показываем статус контекста
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("📦 Управление контекстом:");
                    Console.ResetColor();

                    var cm = agent.ContextManager;
                    var cfg = cm.Config;
                    Console.WriteLine($"   Включено: {(cfg.Enabled ? "да" : "нет")}");
                    Console.WriteLine($"   Активная стратегия: {cfg.Strategy}");
                    Console.WriteLine();

                    // Настройки каждой стратегии
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("   Настройки стратегий:");
                    Console.ResetColor();
                    Console.WriteLine($"   SlidingWindow:     window={config.Context.SlidingWindow.WindowSize}");
                    Console.WriteLine($"   StickyFacts:       window={config.Context.StickyFacts.WindowSize}, maxFacts={config.Context.StickyFacts.MaxFacts}");
                    Console.WriteLine($"   Branching:         maxBranches={config.Context.Branching.MaxBranches}, maxCheckpoints={config.Context.Branching.MaxCheckpoints}");
                    Console.WriteLine($"   Summary (legacy):  interval={config.Context.Summary.Interval}, maxSummaries={config.Context.Summary.MaxSummaries}");

                    // Статус активной стратегии
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("   Статус активной стратегии:");
                    Console.ResetColor();
                    Console.WriteLine(cm.GetStrategyStatus());

                    // Статус legacy summary (если не SlidingWindow)
                    if (cfg.Strategy == ContextStrategy.SlidingWindow || cfg.Strategy == ContextStrategy.Branching)
                    {
                        Console.WriteLine($"   Summary блоков (legacy): {cm.SummaryCount}");
                    }

                    // Метрики сравнения
                    if (agent.Metrics.ContextComparison.ComparisonCount > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("📊 Метрики сжатия:");
                        Console.ResetColor();
                        var comp = agent.Metrics.ContextComparison;
                        Console.WriteLine($"   Сравнений: {comp.ComparisonCount}");
                        Console.WriteLine($"   Токены до:     {comp.TotalOriginalTokens,10:N0}");
                        Console.WriteLine($"   Токены после:  {comp.TotalCompressedTokens,10:N0}");
                        Console.WriteLine($"   Экономия:      {comp.TotalTokenSavings,10:N0} ({comp.TokenSavingsPercent:F1}%)");
                        Console.WriteLine();
                    }

                    Console.WriteLine("   Команды:");
                    Console.WriteLine("   /context strategy — список стратегий");
                    Console.WriteLine("   /context strategy <sliding|sticky|branching> — переключить");
                    Console.WriteLine("   /context on/off (legacy summary)");
                    Console.WriteLine("   /context recent <N>, /context interval <N>, /context report");
                    Console.WriteLine();
                    continue;
                }

                var contextCommand = parts[1].ToLowerInvariant();

                switch (contextCommand)
                {
                    case "strategy":
                        // parts[2] — аргумент стратегии (sliding, sticky, branching)
                        var strategyArg = parts.Length >= 3 ? parts[2] : "";

                        if (strategyArg == "")
                        {
                            // Показываем список стратегий
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("📦 Стратегии управления контекстом:");
                            Console.ResetColor();
                            Console.WriteLine();
                            Console.WriteLine("   1. sliding — Sliding Window");
                            Console.WriteLine("      Хранит только последние N сообщений, остальное отбрасывается.");
                            Console.WriteLine("      Быстрая, без LLM-вызовов.");
                            Console.WriteLine();
                            Console.WriteLine("   2. sticky — Sticky Facts");
                            Console.WriteLine("      Отдельный блок фактов (ключ-значение) + последние N сообщений.");
                            Console.WriteLine("      Факты обновляются LLM после каждого сообщения пользователя.");
                            Console.WriteLine();
                            Console.WriteLine("   3. branching — Branching");
                            Console.WriteLine("      Ветвление диалога: checkpoints, создание веток, переключение.");
                            Console.WriteLine();
                            Console.WriteLine($"   Текущая стратегия: {agent.ContextManager.Config.Strategy}");
                            Console.WriteLine();
                        }
                        else
                        {
                            var strategyName = strategyArg.ToLowerInvariant();
                            ContextStrategy newStrategy;
                            switch (strategyName)
                            {
                                case "sliding":
                                case "slidingwindow":
                                    newStrategy = ContextStrategy.SlidingWindow;
                                    break;
                                case "sticky":
                                case "stickyfacts":
                                    newStrategy = ContextStrategy.StickyFacts;
                                    break;
                                case "branching":
                                    newStrategy = ContextStrategy.Branching;
                                    break;
                                default:
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"❌ Неизвестная стратегия: '{strategyArg}'. Доступны: sliding, sticky, branching");
                                    Console.ResetColor();
                                    Console.WriteLine();
                                    continue;
                            }

                            agent.ContextManager.SetStrategy(newStrategy);
                            agent.ContextManager.Config.Strategy = newStrategy;

                            Console.ForegroundColor = ConsoleColor.Green;
                            var strategyDesc = newStrategy switch
                            {
                                ContextStrategy.SlidingWindow => "Sliding Window (последние N сообщений)",
                                ContextStrategy.StickyFacts => "Sticky Facts (факты + последние N сообщений)",
                                ContextStrategy.Branching => "Branching (ветвление диалога)",
                                _ => "Неизвестная",
                            };
                            Console.WriteLine($"✅ Стратегия изменена на: {strategyDesc}");
                            Console.ResetColor();
                            Console.WriteLine();
                        }
                        break;

                    case "on":
                        contextConfig.Enabled = true;
                        agent.ContextManager.Config.Enabled = true;
                        agent.Metrics.ContextCompressionEnabled = true;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✅ Управление контекстом включено (сжатие с summary)");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "off":
                        contextConfig.Enabled = false;
                        agent.ContextManager.Config.Enabled = false;
                        agent.Metrics.ContextCompressionEnabled = false;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("⚠️  Управление контекстом выключено (полная история)");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "reset":
                        contextConfig.Enabled = false;
                        contextConfig.RecentMessageCount = 10;
                        contextConfig.SummaryInterval = 10;
                        contextConfig.MaxSummaries = 20;
                        contextConfig.MaxContextTokens = 0;
                        contextConfig.Strategy = ContextStrategy.SlidingWindow;
                        agent.ContextManager.Config.Enabled = false;
                        agent.ContextManager.Config.RecentMessageCount = 10;
                        agent.ContextManager.Config.SummaryInterval = 10;
                        agent.ContextManager.Config.MaxSummaries = 20;
                        agent.ContextManager.Config.MaxContextTokens = 0;
                        agent.ContextManager.Config.Strategy = ContextStrategy.SlidingWindow;
                        agent.ContextManager.SetStrategy(ContextStrategy.SlidingWindow);
                        agent.Metrics.ContextCompressionEnabled = false;
                        agent.ContextManager.Clear();
                        agent.Metrics.ContextComparison.Reset();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✅ Параметры контекста сброшены, история очищена");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "recent":
                        if (parts.Length < 3 || !int.TryParse(parts[2], out var recentCount))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /context recent <N>. Пример: /context recent 15");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (recentCount < 1)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Recent должно быть >= 1");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        contextConfig.RecentMessageCount = recentCount;
                        agent.ContextManager.Config.RecentMessageCount = recentCount;
                        // Обновляем размер окна для SlidingWindow
                        if (agent.ContextManager.SlidingWindow is not null)
                        {
                            // Пересоздаём с новым размером
                            var sw = agent.ContextManager.SlidingWindow;
                            // Просто показываем, что размер обновлён
                        }
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Recent сообщений установлен: {recentCount}");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "interval":
                        if (parts.Length < 3 || !int.TryParse(parts[2], out var interval))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /context interval <N>. Пример: /context interval 15");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (interval < 1)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Interval должно быть >= 1");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        contextConfig.SummaryInterval = interval;
                        agent.ContextManager.Config.SummaryInterval = interval;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Интервал summary установлен: каждые {interval} сообщений");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "report":
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("📦 Статус управления контекстом:");
                        Console.ResetColor();
                        var cmReport = agent.ContextManager;
                        var cfgReport = cmReport.Config;
                        Console.WriteLine($"   Стратегия: {cfgReport.Strategy}");
                        Console.WriteLine($"   Recent: {cfgReport.RecentMessageCount}, Interval: {cfgReport.SummaryInterval}");
                        Console.WriteLine($"   Summary блоков: {cmReport.SummaryCount}");
                        Console.WriteLine($"   Всего сообщений: {cmReport.TotalHistoryCount}");
                        Console.WriteLine();

                        if (agent.Metrics.ContextComparison.ComparisonCount > 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("📊 Метрики сжатия:");
                            Console.ResetColor();
                            Console.WriteLine(agent.Metrics.ContextComparison.GetReport());
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.WriteLine("   Пока нет данных (отправьте несколько запросов)");
                            Console.ResetColor();
                        }
                        Console.WriteLine();
                        break;

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Неизвестная команда контекста: {contextCommand}. Доступны: strategy, on, off, reset, recent, interval, report");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;
                }
                continue;

            // === Команды профиля агента ===
            case "/agent-profile":
                if (parts.Length < 2)
                {
                    // Показываем статус профиля
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("🤖 Профиль агента:");
                    Console.ResetColor();
                    var p = agent.AgentProfile;
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
                    continue;
                }

                var profileCommand = parts[1].ToLowerInvariant();

                switch (profileCommand)
                {
                    case "name":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /agent-profile name <имя>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        agent.AgentProfile.Name = string.Join(" ", parts.Skip(2));
                        AgentProfileManager.Save(agent.AgentProfile);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Имя изменено на: {agent.AgentProfile.Name}");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "style":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /agent-profile style <concise|detailed|balanced>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (Enum.TryParse<CommunicationStyle>(parts[2], ignoreCase: true, out var newStyle))
                        {
                            agent.AgentProfile.Style = newStyle;
                            AgentProfileManager.Save(agent.AgentProfile);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"✅ Стиль изменён на: {newStyle}");
                            Console.ResetColor();
                            Console.WriteLine();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"❌ Неизвестный стиль: '{parts[2]}'. Доступны: concise, detailed, balanced");
                            Console.ResetColor();
                            Console.WriteLine();
                        }
                        break;

                    case "format":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /agent-profile format <markdown|plaintext|codeonly|structured>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (Enum.TryParse<OutputFormat>(parts[2], ignoreCase: true, out var newFormat))
                        {
                            agent.AgentProfile.Format = newFormat;
                            AgentProfileManager.Save(agent.AgentProfile);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"✅ Формат изменён на: {newFormat}");
                            Console.ResetColor();
                            Console.WriteLine();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"❌ Неизвестный формат: '{parts[2]}'. Доступны: markdown, plaintext, codeonly, structured");
                            Console.ResetColor();
                            Console.WriteLine();
                        }
                        break;

                    case "language":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /agent-profile language <russian|english|auto>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (Enum.TryParse<ResponseLanguage>(parts[2], ignoreCase: true, out var newLang))
                        {
                            agent.AgentProfile.Language = newLang;
                            AgentProfileManager.Save(agent.AgentProfile);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"✅ Язык изменён на: {newLang}");
                            Console.ResetColor();
                            Console.WriteLine();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"❌ Неизвестный язык: '{parts[2]}'. Доступны: russian, english, auto");
                            Console.ResetColor();
                            Console.WriteLine();
                        }
                        break;

                    case "depth":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /agent-profile depth <beginner|intermediate|expert>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (Enum.TryParse<ExpertiseLevel>(parts[2], ignoreCase: true, out var newExp))
                        {
                            agent.AgentProfile.Depth = newExp;
                            AgentProfileManager.Save(agent.AgentProfile);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"✅ Глубина изменён на: {newExp}");
                            Console.ResetColor();
                            Console.WriteLine();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"❌ Неизвестный уровень: '{parts[2]}'. Доступны: beginner, intermediate, expert");
                            Console.ResetColor();
                            Console.WriteLine();
                        }
                        break;

                    case "domain":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /agent-profile domain <домен>. Пример: /agent-profile domain .NET");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        agent.AgentProfile.Domain = string.Join(" ", parts.Skip(2));
                        AgentProfileManager.Save(agent.AgentProfile);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Домен изменён на: {agent.AgentProfile.Domain}");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "tech":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /agent-profile tech <add|remove|list|clear> [технология]");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        var techAction = parts[2].ToLowerInvariant();
                        switch (techAction)
                        {
                            case "add":
                                if (parts.Length < 4)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("❌ Формат: /agent-profile tech add <технология>");
                                    Console.ResetColor();
                                    Console.WriteLine();
                                    break;
                                }
                                var techToAdd = string.Join(" ", parts.Skip(3));
                                if (!agent.AgentProfile.PreferredTechnologies.Contains(techToAdd, StringComparer.OrdinalIgnoreCase))
                                {
                                    agent.AgentProfile.PreferredTechnologies.Add(techToAdd);
                                    AgentProfileManager.Save(agent.AgentProfile);
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"✅ Добавлена предпочитаемая технология: {techToAdd}");
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine($"⚠️  Технология '{techToAdd}' уже есть в списке");
                                }
                                Console.ResetColor();
                                Console.WriteLine();
                                break;

                            case "remove":
                                if (parts.Length < 4)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("❌ Формат: /agent-profile tech remove <технология>");
                                    Console.ResetColor();
                                    Console.WriteLine();
                                    break;
                                }
                                var techToRemove = string.Join(" ", parts.Skip(3));
                                var countBefore = agent.AgentProfile.PreferredTechnologies.Count;
                                agent.AgentProfile.PreferredTechnologies.RemoveAll(t => t.Equals(techToRemove, StringComparison.OrdinalIgnoreCase));
                                if (agent.AgentProfile.PreferredTechnologies.Count < countBefore)
                                {
                                    AgentProfileManager.Save(agent.AgentProfile);
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"✅ Удалена предпочитаемая технология: {techToRemove}");
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine($"⚠️  Технология '{techToRemove}' не найдена в списке");
                                }
                                Console.ResetColor();
                                Console.WriteLine();
                                break;

                            case "list":
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine("📦 Предпочитаемые технологии:");
                                Console.ResetColor();
                                if (agent.AgentProfile.PreferredTechnologies.Count == 0)
                                {
                                    Console.WriteLine("   (пусто)");
                                }
                                else
                                {
                                    foreach (var tech in agent.AgentProfile.PreferredTechnologies)
                                    {
                                        Console.WriteLine($"   • {tech}");
                                    }
                                }
                                Console.WriteLine();
                                break;

                            case "clear":
                                agent.AgentProfile.PreferredTechnologies.Clear();
                                AgentProfileManager.Save(agent.AgentProfile);
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("✅ Список предпочитаемых технологий очищен");
                                Console.ResetColor();
                                Console.WriteLine();
                                break;

                            default:
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"❌ Неизвестное действие: {techAction}. Доступны: add, remove, list, clear");
                                Console.ResetColor();
                                Console.WriteLine();
                                break;
                        }
                        break;

                    case "constraint":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /agent-profile constraint <ограничение>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        var constraint = string.Join(" ", parts.Skip(2));
                        agent.AgentProfile.ResponseConstraints.Add(constraint);
                        AgentProfileManager.Save(agent.AgentProfile);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Ограничение добавлено: {constraint}");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "req":
                    case "requirement":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /agent-profile req <требование>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        var requirement = string.Join(" ", parts.Skip(2));
                        agent.AgentProfile.ResponseRequirements.Add(requirement);
                        AgentProfileManager.Save(agent.AgentProfile);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Требование добавлено: {requirement}");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "instructions":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /agent-profile instructions <заметки>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        agent.AgentProfile.Instructions = string.Join(" ", parts.Skip(2));
                        AgentProfileManager.Save(agent.AgentProfile);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Инструкции обновлены");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    case "reset":
                        agent.AgentProfile = AgentProfileManager.Load("default");
                        AgentProfileManager.Save(agent.AgentProfile);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✅ Профиль агента сброшен к значениям по умолчанию");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Неизвестная команда профиля: {profileCommand}. Введи /profile для подсказки.");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;
                }
                continue;

            case "/save":
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("💾 Сохранение контекста...");
                    Console.ResetColor();

                    var files = new List<string>();

                    // Сохраняем контекст
                    ContextPersistence.SaveContext(agent);

                    // Собираем список созданных/обновлённых файлов в memory/
                    var memoryDir = "memory";
                    if (Directory.Exists(memoryDir))
                    {
                        foreach (var filePath in Directory.GetFiles(memoryDir, "*.json", SearchOption.AllDirectories))
                        {
                            var relPath = Path.GetRelativePath(memoryDir, filePath);
                            var info = new FileInfo(filePath);
                            var size = info.Length;
                            files.Add($"   memory/{relPath,-28} {size,8} байт");
                        }
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✅ Контекст сохранён в папку memory/:");
                    Console.ResetColor();
                    foreach (var f in files)
                    {
                        Console.WriteLine(f);
                    }
                    Console.WriteLine();
                    break;
                }

            case "/facts":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Команды фактов: /facts list, /facts save <ключ> <значение>, /facts delete <ключ>");
                    Console.WriteLine("   Работают для StickyFacts и Branching стратегий.");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }

                var factsCommand = parts[1].ToLowerInvariant();

                switch (factsCommand)
                {
                    case "list":
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("🧠 Факты:");
                            Console.ResetColor();

                            // Если активна Branching — показываем факты текущей ветки
                            if (agent.ContextManager.Config.Strategy == ContextStrategy.Branching &&
                                agent.ContextManager.Branching is { } branchingFacts)
                            {
                                var facts = branchingFacts.Facts;
                                if (facts.Count == 0)
                                {
                                    Console.WriteLine("   (пусто)");
                                }
                                else
                                {
                                    foreach (var kvp in facts)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                        Console.Write($"   → {kvp.Key}: ");
                                        Console.ResetColor();
                                        Console.WriteLine(kvp.Value);
                                    }
                                }
                                Console.WriteLine();
                                Console.WriteLine($"   Всего фактов: {facts.Count} (ветка: {branchingFacts.ActiveBranchName})");
                            }
                            // Если активна StickyFacts — показываем факты StickyFacts
                            else if (agent.ContextManager.StickyFacts is { } sf)
                            {
                                var facts = sf.Facts;
                                if (facts.Count == 0)
                                {
                                    Console.WriteLine("   (пусто)");
                                }
                                else
                                {
                                    foreach (var kvp in facts)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                        Console.Write($"   → {kvp.Key}: ");
                                        Console.ResetColor();
                                        Console.WriteLine(kvp.Value);
                                    }
                                }
                                Console.WriteLine();
                                Console.WriteLine($"   Всего фактов: {facts.Count}");
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine("   Ни одна стратегия с фактами не активна.");
                                Console.WriteLine("   Переключитесь: /context strategy sticky или /context strategy branching");
                                Console.ResetColor();
                            }
                            Console.WriteLine();
                        }
                        break;

                    case "save":
                        if (parts.Length < 4)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /facts save <ключ> <значение>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }

                        // Сохраняем факт в активную стратегию
                        if (agent.ContextManager.Config.Strategy == ContextStrategy.Branching &&
                            agent.ContextManager.Branching is { } branchingSave)
                        {
                            var saveKey = parts[2];
                            var saveValue = parts[3];
                            branchingSave.SaveFact(saveKey, saveValue);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"✅ Факт сохранён: {saveKey} = \"{saveValue}\" (ветка: {branchingSave.ActiveBranchName})");
                            Console.ResetColor();
                        }
                        else if (agent.ContextManager.StickyFacts is { } stickyFacts2)
                        {
                            var saveKey = parts[2];
                            var saveValue = parts[3];
                            stickyFacts2.SaveFact(saveKey, saveValue);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"✅ Факт сохранён: {saveKey} = \"{saveValue}\"");
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("⚠️  Ни одна стратегия с фактами не активна.");
                            Console.WriteLine("   Переключитесь: /context strategy sticky или /context strategy branching");
                            Console.ResetColor();
                        }
                        Console.WriteLine();
                        break;

                    case "delete":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /facts delete <ключ>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }

                        // Удаляем факт из активной стратегии
                        if (agent.ContextManager.Config.Strategy == ContextStrategy.Branching &&
                            agent.ContextManager.Branching is { } branchingDelete)
                        {
                            var deleteKey = parts[2];
                            if (branchingDelete.DeleteFact(deleteKey))
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"✅ Факт удалён: {deleteKey}");
                                Console.ResetColor();
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"❌ Факт не найден: {deleteKey}");
                                Console.ResetColor();
                            }
                        }
                        else if (agent.ContextManager.StickyFacts is { } stickyFacts3)
                        {
                            var deleteKey = parts[2];
                            if (stickyFacts3.DeleteFact(deleteKey))
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"✅ Факт удалён: {deleteKey}");
                                Console.ResetColor();
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"❌ Факт не найден: {deleteKey}");
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("⚠️  Ни одна стратегия с фактами не активна.");
                            Console.WriteLine("   Переключитесь: /context strategy sticky или /context strategy branching");
                            Console.ResetColor();
                        }
                        Console.WriteLine();
                        break;

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Неизвестная команда фактов: {factsCommand}. Доступны: list, save, delete");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;
                }
                continue;

            case "/branch":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Команды веток: /branch list, /branch create <имя>, /branch switch <id>, /branch checkpoint <имя>, /branch create-from <cp-id> <имя>, /branch delete <id>");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }

                var branchCommand = parts[1].ToLowerInvariant();

                switch (branchCommand)
                {
                    case "list":
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("🌿 Ветки диалога:");
                            Console.ResetColor();

                            if (agent.ContextManager.Branching is { } br)
                            {
                                Console.WriteLine(br.GetStatus());
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine("   Branching не активна. Переключитесь: /context strategy branching");
                                Console.ResetColor();
                            }
                            Console.WriteLine();
                        }
                        break;

                    case "create":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /branch create <имя>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (agent.ContextManager.Branching is { } branching2)
                        {
                            var branchName = parts[2];
                            try
                            {
                                var branchId = branching2.CreateBranch(branchName);
                                branching2.SwitchBranch(branchId);
                                agent.ContextManager.SetStrategy(ContextStrategy.Branching);
                                agent.ContextManager.Config.Strategy = ContextStrategy.Branching;

                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"✅ Ветка создана и активирована: \"{branchName}\" (id: {branchId})");
                                Console.ResetColor();
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"❌ Ошибка: {ex.Message}");
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("⚠️  Branching не активна. Переключитесь: /context strategy branching");
                            Console.ResetColor();
                        }
                        Console.WriteLine();
                        break;

                    case "switch":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /branch switch <id>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (agent.ContextManager.Branching is { } branching3)
                        {
                            var branchId = parts[2];
                            try
                            {
                                branching3.SwitchBranch(branchId);
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"✅ Переключено на ветку: {branching3.ActiveBranchName} ({branchId})");
                                Console.ResetColor();
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"❌ Ошибка: {ex.Message}");
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("⚠️  Branching не активна. Переключитесь: /context strategy branching");
                            Console.ResetColor();
                        }
                        Console.WriteLine();
                        break;

                    case "checkpoint":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /branch checkpoint <имя>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (agent.ContextManager.Branching is { } branching4)
                        {
                            // Убираем кавычки из имени
                            var cpName = parts[2].Trim('\"', '\'');
                            try
                            {
                                var cp = branching4.CreateCheckpoint(cpName);
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"✅ Checkpoint создан: \"{cp.Name}\" (id: {cp.Id}, сообщений: {cp.MessageCount})");
                                Console.ResetColor();
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"❌ Ошибка: {ex.Message}");
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("⚠️  Branching не активна. Переключитесь: /context strategy branching");
                            Console.ResetColor();
                        }
                        Console.WriteLine();
                        break;

                    case "create-from":
                        if (parts.Length < 4)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /branch create-from <cp-id> <имя-ветки>");
                            Console.WriteLine("   Пример: /branch create-from cp-1 \"вариант с Redis\"");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (agent.ContextManager.Branching is { } branching6)
                        {
                            var cpId = parts[2];
                            var newBranchName = parts[3].Trim('\"', '\'');
                            try
                            {
                                var branchId = branching6.CreateBranchFromCheckpoint(cpId, newBranchName);
                                branching6.SwitchBranch(branchId);
                                agent.ContextManager.SetStrategy(ContextStrategy.Branching);
                                agent.ContextManager.Config.Strategy = ContextStrategy.Branching;

                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"✅ Ветка создана от checkpoint {cpId}: \"{newBranchName}\" (id: {branchId})");
                                Console.ResetColor();
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"❌ Ошибка: {ex.Message}");
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("⚠️  Branching не активна. Переключитесь: /context strategy branching");
                            Console.ResetColor();
                        }
                        Console.WriteLine();
                        break;

                    case "delete":
                        if (parts.Length < 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌ Формат: /branch delete <id>");
                            Console.ResetColor();
                            Console.WriteLine();
                            break;
                        }
                        if (agent.ContextManager.Branching is { } branching5)
                        {
                            var branchId = parts[2];
                            try
                            {
                                branching5.DeleteBranch(branchId);
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"✅ Ветка удалена: {branchId}. Активна: {branching5.ActiveBranchName}");
                                Console.ResetColor();
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"❌ Ошибка: {ex.Message}");
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("⚠️  Branching не активна. Переключитесь: /context strategy branching");
                            Console.ResetColor();
                        }
                        Console.WriteLine();
                        break;

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Неизвестная команда веток: {branchCommand}. Доступны: list, create, create-from, switch, checkpoint, delete");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;
                }
                continue;

            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Неизвестная команда: {command}. Доступны: /status, /model, /system, /maxtokens, /stop, /temp, /retry, /retrydelay, /loglevel, /metrics, /adaptive, /planner, /memory, /context, /facts, /branch, /save");
                Console.ResetColor();
                Console.WriteLine();
                continue;
        }
    }

    if (string.IsNullOrEmpty(input))
    {
        continue;
    }

    Console.ForegroundColor = ConsoleColor.Gray;
    Console.Write("⏳ Думает...");
    Console.ResetColor();

    var result = await agent.ProcessRequestAsync(input);

    // Выводим логи после ответа (из буфера)
    var logs = agent.Logger.FlushBuffer();
    AgentLogger.PrintBufferedMessages(logs);

    if (!result.IsSuccess)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine();
        Console.WriteLine($"❌ Ошибка: {result.Error}");
        Console.ResetColor();
        Console.WriteLine();
        continue;
    }

    var duration = result.Duration.TotalSeconds < 1
        ? $"{result.Duration.TotalMilliseconds:F0} мс"
        : $"{result.Duration.TotalSeconds:F1} с";

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("🤖 GigaChat:");
    Console.ResetColor();
    Console.WriteLine($"   {result.Answer}");
    Console.WriteLine();

    // Статистика
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.Write($"   ⏱ {duration}");
    if (result.Source == Source.Cache)
    {
        Console.Write("  |  [из кэша]  |  Токенов не потреблено");
    }
    else if (result.Usage is not null)
    {
        var u = result.Usage;
        Console.Write($"  |  📊 Токены: {u.PromptTokens} в → {u.CompletionTokens} out → {u.TotalTokens} всего");
    }
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine();
}
