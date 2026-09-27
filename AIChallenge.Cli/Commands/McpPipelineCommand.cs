using AIChallenge.Services;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace AIChallenge.Cli.Commands;

/// <summary>
/// Команда для запуска предопределённых MCP-пайплайнов.
/// Демонстрирует взаимодействие с несколькими MCP-серверами.
/// </summary>
public sealed class McpPipelineCommand : CommandHandler
{
    public override string Name => "mcp-pipeline";

    /// <summary>
    /// Единый реестр MCP-инструментов. Пайплайн НЕ знает о конкретных сервисах —
    /// он вызывает инструменты по имени, а реестр маршрутизирует к нужному серверу.
    /// Это и есть настоящее MCP: decoupling между вызывающим кодом и серверами.
    /// </summary>
    public McpToolRegistry? McpRegistry { get; set; }

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            PrintHelp();
            return true;
        }

        var scenario = parts[1].ToLowerInvariant();
        var args = ParseArgs(parts.Skip(2));

        Console.WriteLine();
        PrintCyan($"🚀 MCP Pipeline — сценарий: {scenario}");
        Console.WriteLine($"   Аргументы: {JsonSerializer.Serialize(args)}");
        Console.WriteLine();

        var result = await RunScenarioAsync(scenario, args);

        Console.WriteLine();
        if (result.Success)
        {
            PrintGreen("✅ Пайплайн выполнен успешно");
            Console.WriteLine();
            PrintGray(result.Output);
        }
        else
        {
            PrintRed($"❌ Ошибка: {result.Error}");
        }

        return true;
    }

    private static Dictionary<string, string> ParseArgs(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>();
        var parts = args.ToList();
        for (int i = 0; i < parts.Count; i++)
        {
            var arg = parts[i];
            if (arg.StartsWith("--"))
            {
                var key = arg[2..];
                string value = "";
                if (i + 1 < parts.Count && !parts[i + 1].StartsWith("--"))
                {
                    value = parts[i + 1];
                    i++; // skip next arg
                }
                result[key] = value;
            }
        }
        return result;
    }

    private static void PrintHelp()
    {
        PrintYellow("📋 Использование MCP Pipeline:");
        Console.WriteLine();
        PrintCmd("mcp-pipeline", "project-status --repo AIChallenge", "статус проекта (GitHub + TodoMCP)");
        PrintCmd("mcp-pipeline", "repo-analyze --owner user --repo proj", "анализ репозитория");
        PrintCmd("mcp-pipeline", "todo-report", "отчёт по задачам");
        PrintCmd("mcp-pipeline", "repo-sync --owner Kriger --repo AIChallenge", "синхронизация GitHub → TodoMCP");
        Console.WriteLine();
    }

    private async Task<PipelineResult> RunScenarioAsync(string scenario, Dictionary<string, string> args)
    {
        return scenario switch
        {
            "project-status" => await RunProjectStatusAsync(args),
            "repo-analyze" => await RunRepoAnalyzeAsync(args),
            "todo-report" => await RunTodoReportAsync(args),
            "repo-sync" => await RunRepoSyncAsync(args),
            _ => new PipelineResult(false, "Неизвестный сценарий", $"Используйте: project-status, repo-analyze, todo-report, repo-sync")
        };
    }

    // ─── Сценарий 1: Project Status ──────────────────────────────────────

    private async Task<PipelineResult> RunProjectStatusAsync(Dictionary<string, string> args)
    {
        var repo = args.GetValueOrDefault("repo", "AIChallenge");
        var owner = args.GetValueOrDefault("owner", "kriger");

        var sb = new StringBuilder();
        var steps = new List<string>();

        // Шаг 1: GitHub — последние коммиты
        sb.AppendLine("━━━ Шаг 1/4: list_commits (GitHub MCP) ━━━");
        Console.WriteLine("📦 Загрузка последних коммитов из GitHub...");
        var commitsResult = await SafeCallAsync("list-commits", new Dictionary<string, object?>
        {
            ["owner"] = owner,
            ["repo"] = repo,
            ["limit"] = 10
        });
        if (commitsResult.Success)
        {
            steps.Add("list_commits");
            sb.AppendLine(commitsResult.Output);
            Console.WriteLine($"✅ Загружено коммитов");
        }
        else
        {
            sb.AppendLine($"❌ Ошибка: {commitsResult.Error}");
            Console.WriteLine($"❌ {commitsResult.Error}");
        }
        Console.WriteLine();

        // Шаг 2: GitHub — open issues
        sb.AppendLine("━━━ Шаг 2/4: list_issues (GitHub MCP) ━━━");
        Console.WriteLine("📦 Загрузка open issues из GitHub...");
        var issuesResult = await SafeCallAsync("list_issues", new Dictionary<string, object?>
        {
            ["owner"] = owner,
            ["repo"] = repo,
            ["state"] = "open"
        });
        if (issuesResult.Success)
        {
            steps.Add("list_issues");
            sb.AppendLine(issuesResult.Output);
            Console.WriteLine($"✅ Загружено issues");
        }
        else
        {
            sb.AppendLine($"❌ Ошибка: {issuesResult.Error}");
            Console.WriteLine($"❌ {issuesResult.Error}");
        }
        Console.WriteLine();

        // Шаг 3: TodoMCP — все задачи
        sb.AppendLine("━━━ Шаг 3/4: list_todo_items (TodoMCP) ━━━");
        Console.WriteLine("📋 Загрузка задач из TodoMCP...");
        var tasksResult = await SafeCallAsync("list_todo_items", new Dictionary<string, object?>());
        if (tasksResult.Success)
        {
            steps.Add("list_todo_items");
            sb.AppendLine(tasksResult.Output);
            Console.WriteLine($"✅ Загружено задач");
        }
        else
        {
            sb.AppendLine($"❌ Ошибка: {tasksResult.Error}");
            Console.WriteLine($"❌ {tasksResult.Error}");
        }
        Console.WriteLine();

        // Шаг 4: TodoMCP — данные проекта
        sb.AppendLine("━━━ Шаг 4/4: get_project (TodoMCP) ━━━");
        Console.WriteLine("📋 Загрузка данных проекта...");
        var projectResult = await SafeCallAsync("get_project", new Dictionary<string, object?>
        {
            ["title"] = repo
        });
        if (projectResult.Success)
        {
            steps.Add("get_project");
            sb.AppendLine(projectResult.Output);
            Console.WriteLine($"✅ Проект найден");
        }
        else
        {
            sb.AppendLine($"❌ Ошибка: {projectResult.Error}");
            Console.WriteLine($"❌ {projectResult.Error}");
        }
        Console.WriteLine();

        // Финальная сводка
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"📊 СВОДКА ПО ПРОЕКТУ {repo.ToUpper()}");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine();
        sb.AppendLine($"Инструменты использованы: {string.Join(" → ", steps)}");
        sb.AppendLine($"Серверы: GitHub MCP → TodoMCP");
        sb.AppendLine();

        return new PipelineResult(true, sb.ToString(), "OK");
    }

    // ─── Сценарий 2: Repo Analyze ────────────────────────────────────────

    private async Task<PipelineResult> RunRepoAnalyzeAsync(Dictionary<string, string> args)
    {
        var owner = args.GetValueOrDefault("owner", "kriger");
        var repo = args.GetValueOrDefault("repo", "AIChallenge");

        var sb = new StringBuilder();
        var steps = new List<string>();

        // Шаг 1: GitHub — README
        sb.AppendLine("━━━ Шаг 1/4: get-file — README (GitHub MCP) ━━━");
        Console.WriteLine("📦 Загрузка README из GitHub...");
        var readmeResult = await SafeCallAsync("get-file", new Dictionary<string, object?>
        {
            ["owner"] = owner,
            ["repo"] = repo,
            ["path"] = "README.md"
        });
        if (readmeResult.Success)
        {
            steps.Add("get_file_readme");
            sb.AppendLine(readmeResult.Output);
            Console.WriteLine("✅ README загружен");
        }
        else
        {
            sb.AppendLine($"⚠️ README не найден или ошибка: {readmeResult.Error}");
            Console.WriteLine($"⚠️ {readmeResult.Error}");
        }
        Console.WriteLine();

        // Шаг 2: GitHub — последние коммиты
        sb.AppendLine("━━━ Шаг 2/4: list_commits (GitHub MCP) ━━━");
        Console.WriteLine("📦 Загрузка последних коммитов...");
        var commitsResult = await SafeCallAsync("list-commits", new Dictionary<string, object?>
        {
            ["owner"] = owner,
            ["repo"] = repo,
            ["limit"] = 5
        });
        if (commitsResult.Success)
        {
            steps.Add("list_commits");
            sb.AppendLine(commitsResult.Output);
            Console.WriteLine("✅ Коммиты загружены");
        }
        else
        {
            sb.AppendLine($"❌ Ошибка: {commitsResult.Error}");
        }
        Console.WriteLine();

        // Шаг 3: GitHub — open issues
        sb.AppendLine("━━━ Шаг 3/4: list_issues (GitHub MCP) ━━━");
        Console.WriteLine("📦 Загрузка open issues...");
        var issuesResult = await SafeCallAsync("list_issues", new Dictionary<string, object?>
        {
            ["owner"] = owner,
            ["repo"] = repo,
            ["state"] = "open"
        });
        if (issuesResult.Success)
        {
            steps.Add("list_issues");
            sb.AppendLine(issuesResult.Output);
            Console.WriteLine("✅ Issues загружены");
        }
        else
        {
            sb.AppendLine($"❌ Ошибка: {issuesResult.Error}");
        }
        Console.WriteLine();

        // Шаг 4: TodoMCP — проекты
        sb.AppendLine("━━━ Шаг 4/4: list_projects (TodoMCP) ━━━");
        Console.WriteLine("📋 Загрузка проектов из TodoMCP...");
        var projectsResult = await SafeCallAsync("list_projects", new Dictionary<string, object?>());
        if (projectsResult.Success)
        {
            steps.Add("list_projects");
            sb.AppendLine(projectsResult.Output);
            Console.WriteLine("✅ Проекты загружены");
        }
        else
        {
            sb.AppendLine($"❌ Ошибка: {projectsResult.Error}");
        }
        Console.WriteLine();

        // Сводка
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"📊 АНАЛИЗ РЕПОЗИТОРИЯ {owner}/{repo}");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine();
        sb.AppendLine($"Инструменты: {string.Join(" → ", steps)}");
        sb.AppendLine($"Серверы: GitHub MCP (3 вызова) → TodoMCP (1 вызов)");
        sb.AppendLine();

        return new PipelineResult(true, sb.ToString(), "OK");
    }

    // ─── Сценарий 3: Todo Report ─────────────────────────────────────────

    private async Task<PipelineResult> RunTodoReportAsync(Dictionary<string, string> args)
    {
        var sb = new StringBuilder();
        var steps = new List<string>();

        // Шаг 1: TodoMCP — текущий пользователь
        sb.AppendLine("━━━ Шаг 1/3: get_current_user (TodoMCP) ━━━");
        Console.WriteLine("📋 Загрузка данных текущего пользователя...");
        var userResult = await SafeCallAsync("get_current_user", new Dictionary<string, object?>());
        if (userResult.Success)
        {
            steps.Add("get_current_user");
            sb.AppendLine(userResult.Output);
            Console.WriteLine("✅ Данные пользователя загружены");
        }
        else
        {
            sb.AppendLine($"⚠️ Ошибка: {userResult.Error}");
        }
        Console.WriteLine();

        // Шаг 2: TodoMCP — все задачи
        sb.AppendLine("━━━ Шаг 2/3: list_todo_items (TodoMCP) ━━━");
        Console.WriteLine("📋 Загрузка всех задач...");
        var tasksResult = await SafeCallAsync("list_todo_items", new Dictionary<string, object?>());
        if (tasksResult.Success)
        {
            steps.Add("list_todo_items");
            sb.AppendLine(tasksResult.Output);
            Console.WriteLine("✅ Задачи загружены");
        }
        else
        {
            sb.AppendLine($"❌ Ошибка: {tasksResult.Error}");
        }
        Console.WriteLine();

        // Шаг 3: TodoMCP — проекты
        sb.AppendLine("━━━ Шаг 3/3: list_projects (TodoMCP) ━━━");
        Console.WriteLine("📋 Загрузка проектов...");
        var projectsResult = await SafeCallAsync("list_projects", new Dictionary<string, object?>());
        if (projectsResult.Success)
        {
            steps.Add("list_projects");
            sb.AppendLine(projectsResult.Output);
            Console.WriteLine("✅ Проекты загружены");
        }
        else
        {
            sb.AppendLine($"❌ Ошибка: {projectsResult.Error}");
        }
        Console.WriteLine();

        // Сводка
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("📊 ОТЧЁТ ПО ЗАДАЧАМ");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine();
        sb.AppendLine($"Инструменты: {string.Join(" → ", steps)}");
        sb.AppendLine($"Сервер: TodoMCP (3 вызова)");
        sb.AppendLine();

        return new PipelineResult(true, sb.ToString(), "OK");
    }

    // ─── Сценарий 4: Repo Sync (GitHub → TodoMCP) ────────────────────────
    // Синхронизация: берём конкретный репозиторий, смотрим issues,
    // создаём проекты и задачи в TodoMCP при необходимости

    private async Task<PipelineResult> RunRepoSyncAsync(Dictionary<string, string> args)
    {
        var owner = args.GetValueOrDefault("owner", "Kriger");
        var repo = args.GetValueOrDefault("repo", "AIChallenge");

        var sb = new StringBuilder();
        var steps = new List<string>();
        var syncStats = new SyncStats();

        // Шаг 1: Проверка репозитория
        sb.AppendLine("━━━ Шаг 1: Проверка репозитория ━━━");
        Console.WriteLine($"📦 Проверка репозитория {owner}/{repo}...");
        
        var repoNames = new List<string> { $"{owner}/{repo}" };
        Console.WriteLine($"   ✅ Репозиторий: {owner}/{repo}");
        Console.WriteLine();

        // Шаг 2: Для каждого репозитория — проверяем issues
        sb.AppendLine("━━━ Шаг 2: Проверка issues в каждом репозитории ━━━");
        Console.WriteLine();

        var reposToProcess = new List<(string Name, List<(string Title, string? Description)> Issues)>();

        foreach (var repoName in repoNames)
        {
            Console.WriteLine($"   📦 Обрабатываем: {repoName}...");
            syncStats.ReposChecked++;

            // Извлекаем owner и repo из full_name (например "Kriger/AIChallenge")
            var parts = repoName.Split('/');
            var repoOwner = parts.Length >= 2 ? parts[0] : owner;
            var repoFullName = parts.Length >= 2 ? parts[1] : repoName;

            var issuesListResult = await SafeCallAsync("list_issues", new Dictionary<string, object?>
            {
                ["owner"] = repoOwner,
                ["repo"] = repoFullName,
                ["state"] = "open"
            });

            if (!issuesListResult.Success)
            {
                Console.WriteLine($"      ⚠️ Ошибка: {issuesListResult.Error}");
                continue;
            }

            steps.Add("list_issues");

            // Парсим issues — извлекаем title и body
            List<(string Title, string? Description)> issues = [];
            try
            {
                using var doc = JsonDocument.Parse(issuesListResult.Output);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var issue in root.EnumerateArray())
                    {
                        string? title = null;
                        if (issue.TryGetProperty("title", out var t))
                            title = t.GetString();
                        if (string.IsNullOrEmpty(title) && issue.TryGetProperty("number", out var n))
                            title = $"#{n.GetInt32()}";

                        string? body = null;
                        if (issue.TryGetProperty("body", out var b))
                            body = b.GetString();
                        if (string.IsNullOrEmpty(body) && issue.TryGetProperty("body_text", out var bt))
                            body = bt.GetString();

                        if (!string.IsNullOrEmpty(title))
                            issues.Add((title, body));
                    }
                }
            }
            catch { /* ignore parse errors */ }

            if (issues.Count > 0)
            {
                syncStats.ReposWithIssues++;
                Console.WriteLine($"      ✅ Найдено issues: {issues.Count}");
                reposToProcess.Add((repoName, issues));
            }
            else
            {
                Console.WriteLine($"      ℹ️  Open issues не найдены");
            }
            Console.WriteLine();
        }

        if (reposToProcess.Count == 0)
        {
            sb.AppendLine("ℹ️  Open issues не найдены ни в одном репозитории");
            sb.AppendLine();
            sb.AppendLine($"Статистика: {syncStats.ReposChecked} репозиториев проверено, 0 с issues");
            return new PipelineResult(true, sb.ToString(), null);
        }

        // Шаг 3: Для каждого репозитория с issues — создаём проект и задачи
        sb.AppendLine("━━━ Шаг 3: Создание проектов и задач в TodoMCP ━━━");
        Console.WriteLine();

        foreach (var (repoName, issues) in reposToProcess)
        {
            Console.WriteLine($"   📋 Репозиторий: {repoName} ({issues.Count} issues)");

            // 3a. Проверяем, существует ли проект в TodoMCP
            var projectCheck = await SafeCallAsync("list_projects", new Dictionary<string, object?>());
            steps.Add("list_projects");
            bool projectExists = false;
            int? projectId = null;

            if (projectCheck.Success)
            {
                try
                {
                    using var doc = JsonDocument.Parse(projectCheck.Output);
                    foreach (var project in doc.RootElement.EnumerateArray())
                    {
                        string? title = null;
                        // Пробуем несколько вариантов ключей (сервер использует PascalCase)
                        foreach (var key in new[] { "Title", "title", "Name", "name", "Subject", "subject" })
                        {
                            if (project.TryGetProperty(key, out var t))
                            {
                                title = t.GetString();
                                if (!string.IsNullOrEmpty(title)) break;
                            }
                        }
                        if (title != null && title.Equals(repoName, StringComparison.OrdinalIgnoreCase))
                        {
                            projectExists = true;
                            // Извлекаем ID
                            foreach (var key in new[] { "Id", "id", "ID", "ProjectId", "projectid" })
                            {
                                if (project.TryGetProperty(key, out var id))
                                {
                                    projectId = id.GetInt32();
                                    break;
                                }
                            }
                            Console.WriteLine($"      ✅ Проект '{repoName}' уже существует (ID: {projectId})");
                            break;
                        }
                    }
                }
                catch { /* ignore */ }
            }

            // 3b. Если проекта нет — создаём
            if (!projectExists)
            {
                Console.WriteLine($"      🆕 Создаём проект '{repoName}'...");
                var createProjectResult = await SafeCallAsync("create_project", new Dictionary<string, object?>
                {
                    ["title"] = repoName,
                    ["description"] = $"Проект для репозитория {owner}/{repoName} на GitHub"
                });

                if (createProjectResult.Success)
                {
                    projectExists = true;
                    syncStats.ProjectsCreated++;
                    steps.Add("create_project");
                    Console.WriteLine($"      ✅ Проект создан");

                    // Извлекаем ID созданного проекта
                    try
                    {
                        using var doc = JsonDocument.Parse(createProjectResult.Output);
                        foreach (var key in new[] { "Id", "id", "ID", "ProjectId", "projectid" })
                        {
                            if (doc.RootElement.TryGetProperty(key, out var id))
                            {
                                projectId = id.GetInt32();
                                break;
                            }
                        }
                    }
                    catch { /* ignore */ }
                }
                else
                {
                    Console.WriteLine($"      ❌ Ошибка создания проекта: {createProjectResult.Error}");
                }
            }

            // 3c. Для каждого issue — создаём задачу (если нет)
            foreach (var (issueTitle, issueBody) in issues)
            {
                // Проверяем, существует ли уже задача
                var tasksCheck = await SafeCallAsync("list_todo_items", new Dictionary<string, object?>
                {
                    ["project"] = repoName
                });
                steps.Add("list_todo_items");

                bool taskExists = false;
                if (tasksCheck.Success)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(tasksCheck.Output);
                        foreach (var task in doc.RootElement.EnumerateArray())
                        {
                            string? title = null;
                            // Пробуем несколько вариантов ключей
                            foreach (var key in new[] { "Title", "title", "Name", "name" })
                            {
                                if (task.TryGetProperty(key, out var t))
                                {
                                    title = t.GetString();
                                    if (!string.IsNullOrEmpty(title)) break;
                                }
                            }
                            if (title != null && (title.Contains(issueTitle.Split('#').Last()) || title.Contains(issueTitle)))
                            {
                                taskExists = true;
                                Console.WriteLine($"      ⏭️  Задача '{issueTitle}' уже существует");
                                syncStats.TasksSkipped++;
                                break;
                            }
                        }
                    }
                    catch { /* ignore */ }
                }

                if (!taskExists)
                {
                    Console.WriteLine($"      ➕ Создаём задачу: {issueTitle}");
                    var createTaskResult = await SafeCallAsync("create_todo_item", new Dictionary<string, object?>
                    {
                        ["title"] = issueTitle,
                        ["description"] = issueBody ?? $"Issue из {owner}/{repo}: {issueTitle}",
                        ["project"] = repoName,
                        ["priority"] = "High"
                    });

                    if (createTaskResult.Success)
                    {
                        syncStats.TasksCreated++;
                        steps.Add("create_todo_item");
                        Console.WriteLine($"      ✅ Задача создана");
                    }
                    else
                    {
                        Console.WriteLine($"      ❌ Ошибка создания задачи: {createTaskResult.Error}");
                    }
                }
            }

            Console.WriteLine();
        }

        // Финальная сводка
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("📊 СВОДКА ПО СИНХРОНИЗАЦИИ");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine();
        sb.AppendLine($"Репозиториев проверено: {syncStats.ReposChecked}");
        sb.AppendLine($"Репозиториев с issues: {syncStats.ReposWithIssues}");
        sb.AppendLine($"Проектов создано: {syncStats.ProjectsCreated}");
        sb.AppendLine($"Задач создано: {syncStats.TasksCreated}");
        sb.AppendLine($"Задач пропущено (уже существуют): {syncStats.TasksSkipped}");
        sb.AppendLine();
        sb.AppendLine($"Инструменты: {string.Join(" → ", steps)}");
        sb.AppendLine($"Серверы: {string.Join(" → ", steps.Select(s =>
        {
            var tool = McpRegistry?.Tools.FirstOrDefault(t => t.Name.Equals(s, StringComparison.OrdinalIgnoreCase));
            return tool?.ServiceName ?? "unknown";
        }).Distinct())}");
        sb.AppendLine();
        sb.AppendLine("🔧 Архитектура: пайплайн вызывает инструменты через MCP-реестр,");
        sb.AppendLine("   который маршрутизирует вызовы к нужным серверам (GitHub MCP, TodoMCP).");
        sb.AppendLine("   Пайплайн НЕ знает о конкретных сервисах — это decoupling MCP.");
        sb.AppendLine();

        return new PipelineResult(true, sb.ToString(), null);
    }

    // ─── Утилита: вызов инструмента через MCP-реестр ────────────────────
    // Этот метод НЕ знает о конкретных сервисах — он вызывает инструменты
    // через единый реестр. Именно это и есть настоящее MCP: decoupling.

    private async Task<ToolCallResult> SafeCallAsync(string toolName, IReadOnlyDictionary<string, object?>? args)
    {
        try
        {
            if (McpRegistry == null)
                return ToolCallResult.Fail($"MCP-реестр не инициализирован");

            // Получаем информацию об инструменте из реестра
            var tool = McpRegistry.Tools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
            if (tool == null)
                return ToolCallResult.Fail($"Инструмент '{toolName}' не найден в MCP-реестре");

            Console.WriteLine($"   📡 MCP: вызываем '{toolName}' ({tool.ServiceName})...");

            // Преобразуем аргументы в JSON
            var argsJson = args != null ? JsonSerializer.Serialize(args, new JsonSerializerOptions { WriteIndented = false }) : null;

            var result = await McpRegistry.ExecuteToolCall(toolName, argsJson);
            return ToolCallResult.Ok(result);
        }
        catch (Exception ex)
        {
            return ToolCallResult.Fail(ex.Message);
        }
    }
}

// ─── Вспомогательные типы ───────────────────────────────────────────────

internal record PipelineResult(bool Success, string Output, string? Error = null);

internal class ToolCallResult
{
    public bool Success { get; }
    public string Output { get; }
    public string? Error { get; }

    private ToolCallResult(bool success, string output, string? error)
    {
        Success = success;
        Output = output;
        Error = error;
    }

    public static ToolCallResult Ok(string output) => new(true, output, null);
    public static ToolCallResult Fail(string error) => new(false, "", error);
}

/// <summary>
/// Статистика для сценария repo-sync.
/// </summary>
internal class SyncStats
{
    public int ReposChecked { get; set; }
    public int ReposWithIssues { get; set; }
    public int ProjectsCreated { get; set; }
    public int TasksCreated { get; set; }
    public int TasksSkipped { get; set; }
}
