using AIChallenge.McpPipeline.Tools;
using AIChallenge.McpScheduler;
using AIChallenge.Models;
using AIChallenge.Services;
using Microsoft.Extensions.Configuration;

namespace AIChallenge.McpPipeline;

/// <summary>
/// Результат выполнения пайплайна.
/// </summary>
public record PipelineResult(
    bool Success,
    string Output,
    Dictionary<string, string> StepResults,
    TimeSpan Duration,
    string? Error = null
);

/// <summary>
/// Описание шага пайплайна.
/// </summary>
public record PipelineStep(
    string ToolName,
    Dictionary<string, object?> Parameters
);

/// <summary>
/// Оркестратор MCP-пайплайна.
/// Выполняет цепочку инструментов, передавая данные между шагами.
/// </summary>
public sealed class PipelineExecutor : IDisposable
{
    private readonly Dictionary<string, IPipelineTool> _tools;
    private readonly McpTodoService? _mcpService;
    private readonly Action<string> _log;
    private readonly Dictionary<string, string> _stepResults;
    private bool _disposed;

    public PipelineExecutor(
        IConfiguration? configuration = null,
        string? baseDirectory = null,
        Action<string>? log = null,
        LlmSummaryService? llmService = null)
    {
        _log = log ?? (msg => Console.WriteLine($"  [pipeline] {msg}"));
        _stepResults = new Dictionary<string, string>();

        // Создаём McpTodoService один раз и передаём в SearchTool и EnrichTool
        McpTodoService? mcpService = null;
        if (configuration != null)
        {
            try
            {
                mcpService = new McpTodoService(
                    (msg, level) =>
                    {
                        if (level == LogLevel.Warning && msg.Contains("Конфигурация"))
                            return;
                        if (level == LogLevel.Error)
                            _log($"❌ {msg}");
                    },
                    configuration
                );
            }
            catch (Exception ex)
            {
                _log($"⚠️ Не удалось создать McpTodoService: {ex.Message}");
            }
        }

        _mcpService = mcpService;

        _tools = new Dictionary<string, IPipelineTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["search"] = new SearchTool(mcpService, msg => _log(msg)),
            ["enrich"] = new EnrichTool(llmService, mcpService, msg => _log(msg)),

            ["summarize"] = new SummarizeTool(
                baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory,
                msg => _log(msg),
                llmService
            ),

            ["savetofile"] = new SaveToFileTool(msg => _log(msg))
        };
    }

    /// <summary>
    /// Выполняет пайплайн с заданными шагами.
    /// </summary>
    public async Task<PipelineResult> ExecuteAsync(IEnumerable<PipelineStep> steps, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _stepResults.Clear();

        _log("🚀 Запуск пайплайна");
        _log($"   Шагов: {steps.Count()}");

        try
        {
            string? lastOutput = null;

            foreach (var step in steps)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return CreateCancelledResult(stopwatch);
                }

                if (!_tools.TryGetValue(step.ToolName, out var tool))
                {
                    throw new InvalidOperationException($"Неизвестный инструмент: '{step.ToolName}'. Доступны: {string.Join(", ", _tools.Keys)}");
                }

                _log($"━━━ Шаг: {step.ToolName} ━━━");

                var parameters = new Dictionary<string, object?>(step.Parameters);
                AutoWireResult(parameters, lastOutput, step.ToolName);

                var result = await ExecuteToolAsync(tool, parameters);
                _stepResults[step.ToolName] = tool.Summary;
                lastOutput = result;

                _log($"   ✅ Результат: {result.Length} символов");
            }

            stopwatch.Stop();
            _log($"🏁 Пайплайн завершён за {stopwatch.Elapsed.TotalMilliseconds:F0}мс");

            return new PipelineResult(
                Success: true,
                Output: lastOutput ?? string.Empty,
                StepResults: new Dictionary<string, string>(_stepResults),
                Duration: stopwatch.Elapsed
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _log($"❌ Ошибка пайплайна: {ex.Message}");

            return new PipelineResult(
                Success: false,
                Output: string.Empty,
                StepResults: new Dictionary<string, string>(_stepResults),
                Duration: stopwatch.Elapsed,
                Error: ex.Message
            );
        }
    }

    private static void AutoWireResult(Dictionary<string, object?> parameters, string? lastOutput, string currentTool)
    {
        if (lastOutput == null || parameters.ContainsKey("tasksJson"))
            return;

        if (currentTool is "summarize" or "enrich")
            parameters["tasksJson"] = lastOutput;
        else if (currentTool == "saveToFile")
            parameters["content"] = lastOutput;
    }

    private static async Task<string> ExecuteToolAsync(IPipelineTool tool, Dictionary<string, object?> parameters)
    {
        return await tool.ExecuteAsync(parameters);
    }

    private static PipelineResult CreateCancelledResult(System.Diagnostics.Stopwatch stopwatch) =>
        new(
            Success: false,
            Output: "Пайплайн отменён",
            StepResults: new Dictionary<string, string>(),
            Duration: stopwatch.Elapsed,
            Error: "Cancelled"
        );

    // ─── Готовые пайплайны ───────────────────────────────────────────────

    public async Task<PipelineResult> RunStandardPipelineAsync(
        string outputPath,
        string summarizeMode = "stats",
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(StandardPipelineSteps(outputPath, summarizeMode), cancellationToken);
    }

    public async Task<PipelineResult> RunLlmPipelineAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(LlmPipelineSteps(outputPath), cancellationToken);
    }

    public async Task<PipelineResult> RunSearchSavePipelineAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(SearchSavePipelineSteps(outputPath), cancellationToken);
    }

    public async Task<PipelineResult> RunFullPipelineAsync(
        string outputPath,
        string summarizeMode = "stats",
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(FullPipelineSteps(outputPath, summarizeMode), cancellationToken);
    }

    public async Task<PipelineResult> RunFullLlmPipelineAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(FullLlmPipelineSteps(outputPath), cancellationToken);
    }

    // ─── Фабрики шагов ───────────────────────────────────────────────────

    private static IEnumerable<PipelineStep> StandardPipelineSteps(string outputPath, string mode) => new[]
    {
        Step("search", new Dictionary<string, object?>()),
        Step("summarize", new Dictionary<string, object?> { ["mode"] = mode }),
        Step("saveToFile", new Dictionary<string, object?> { ["path"] = outputPath, ["format"] = "text" })
    };

    private static IEnumerable<PipelineStep> LlmPipelineSteps(string outputPath) => new[]
    {
        Step("search", new Dictionary<string, object?>()),
        Step("summarize", new Dictionary<string, object?> { ["mode"] = "llm" }),
        Step("saveToFile", new Dictionary<string, object?> { ["path"] = outputPath, ["format"] = "markdown" })
    };

    private static IEnumerable<PipelineStep> SearchSavePipelineSteps(string outputPath) => new[]
    {
        Step("search", new Dictionary<string, object?>()),
        Step("saveToFile", new Dictionary<string, object?> { ["path"] = outputPath, ["format"] = "json" })
    };

    private static IEnumerable<PipelineStep> FullPipelineSteps(string outputPath, string mode) => new[]
    {
        Step("search", new Dictionary<string, object?>()),
        Step("enrich", new Dictionary<string, object?> { ["mode"] = "both", ["applyChanges"] = true }),
        Step("summarize", new Dictionary<string, object?> { ["mode"] = mode }),
        Step("saveToFile", new Dictionary<string, object?> { ["path"] = outputPath, ["format"] = "text" })
    };

    private static IEnumerable<PipelineStep> FullLlmPipelineSteps(string outputPath) => new[]
    {
        Step("search", new Dictionary<string, object?>()),
        Step("enrich", new Dictionary<string, object?> { ["mode"] = "prioritize", ["applyChanges"] = true }),
        Step("summarize", new Dictionary<string, object?> { ["mode"] = "llm" }),
        Step("saveToFile", new Dictionary<string, object?> { ["path"] = outputPath, ["format"] = "markdown" })
    };

    private static PipelineStep Step(string name, Dictionary<string, object?> parameters) =>
        new(name, parameters);

    /// <summary>
    /// Получает результаты всех шагов.
    /// </summary>
    public Dictionary<string, string> GetStepResults() => new(_stepResults);

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var tool in _tools.Values)
        {
            if (tool is IDisposable disposable)
                disposable.Dispose();
        }
        _disposed = true;
    }
}
