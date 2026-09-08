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
Console.WriteLine("   Память: /memory list, /memory save <ключ> <значение>, /memory delete <ключ>, /memory search <запрос>");
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
var memory = new Memory(logger);
var agent = new ChatAgent(chatClient, authClient, cache, logger, memory);
var adaptive = new AdaptiveBehavior(agent.Metrics, cache, logger);
var planner = new Planner(chatClient, authClient, config.Model, logger, memory);
agent.Adaptive = adaptive;
agent.Planner = planner;

// Инициализация агента из config
agent.Model = config.Model;
agent.SystemMessage = config.SystemMessage;
agent.MaxTokens = config.MaxTokens;
agent.Temperature = config.Temperature;
agent.StopSequences = config.StopSequences;

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

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("👋 До свидания!");
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
        var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
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
                Console.WriteLine("📈 Метрики:");
                Console.ResetColor();
                var m = agent.Metrics;
                Console.WriteLine($"   Запросов: {m.TotalRequests}  |  Успешно: {m.SuccessfulRequests}  |  Ошибок: {m.FailedRequests}");
                Console.WriteLine($"   Из кэша: {m.CachedRequests}  |  Retry: {m.RetryAttempts}");
                Console.WriteLine($"   Success rate: {m.SuccessRate:P1}");
                Console.WriteLine($"   Avg duration: {m.TotalDuration.TotalMilliseconds:F0} мс");
                Console.WriteLine($"   Токены: {m.TotalPromptTokens} in → {m.TotalCompletionTokens} out");
                Console.WriteLine($"   Кэш: {agent.Cache.Count} записей");
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
                Console.WriteLine($"   Кэш: {agent.Cache.Count} записей");
                Console.WriteLine($"   Память: {agent.Memory.Count} фактов");
                Console.WriteLine();
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

            case "/memory":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Команды памяти: /memory list, /memory save <ключ> <значение>, /memory delete <ключ>, /memory search <запрос>");
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
                        var searchResults = agent.Memory.FindRelevant(searchQuery);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"🔍 Поиск по памяти: \"{searchQuery}\"");
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

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Неизвестная команда памяти: {memoryCommand}. Доступны: list, save, delete, search");
                        Console.ResetColor();
                        Console.WriteLine();
                        break;
                }
                continue;

            case "/save":
                ContextPersistence.SaveContext(agent);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ Контекст сохранён.");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Неизвестная команда: {command}. Доступны: /status, /model, /system, /maxtokens, /stop, /temp, /retry, /retrydelay, /loglevel, /metrics, /adaptive, /planner, /memory, /save");
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
        Console.Write("  |  [из кэша]");
    }
    if (result.Usage is not null)
    {
        var u = result.Usage;
        Console.Write($"  |  📊 Токены: {u.PromptTokens} в → {u.CompletionTokens} out → {u.TotalTokens} всего");
    }
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine();
}
