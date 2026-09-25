using AIChallenge.McpPipeline.Tools;
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
    private readonly SearchTool? _searchTool;
    private readonly SummarizeTool? _summarizeTool;
    private readonly SaveToFileTool? _saveToFileTool;
    private readonly Action<string> _log;
    private readonly Dictionary<string, string> _stepResults;
    private bool _disposed;

    public PipelineExecutor(
        IConfiguration? configuration = null,
        string? baseDirectory = null,
        Action<string>? log = null,
        McpScheduler.LlmSummaryService? llmService = null)
    {
        _log = log ?? (msg => Console.WriteLine($"  [pipeline] {msg}"));
        _stepResults = new Dictionary<string, string>();

        _searchTool = configuration != null
            ? new SearchTool(configuration, msg => _log(msg))
            : new SearchTool(log: msg => _log(msg));

        _summarizeTool = new SummarizeTool(
            baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory,
            msg => _log(msg),
            llmService
        );

        _saveToFileTool = new SaveToFileTool(msg => _log(msg));
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
                    return new PipelineResult(
                        Success: false,
                        Output: "Пайплайн отменён",
                        StepResults: new Dictionary<string, string>(_stepResults),
                        Duration: stopwatch.Elapsed,
                        Error: "Cancelled"
                    );
                }

                _log($"━━━ Шаг: {step.ToolName} ━━━");

                // Копируем параметры, добавляя результат предыдущего шага
                var parameters = new Dictionary<string, object?>(step.Parameters);

                // Если это не первый шаг — передаём результат предыдущего
                if (lastOutput != null && !parameters.ContainsKey("tasksJson"))
                {
                    // Для SummarizeTool: передаём как tasksJson
                    if (step.ToolName.Equals("summarize", StringComparison.OrdinalIgnoreCase))
                    {
                        parameters["tasksJson"] = lastOutput;
                    }
                    // Для SaveToFileTool: передаём как content
                    else if (step.ToolName.Equals("saveToFile", StringComparison.OrdinalIgnoreCase))
                    {
                        parameters["content"] = lastOutput;
                    }
                }

                // Выполняем шаг
                var result = await ExecuteStepAsync(step.ToolName, parameters, cancellationToken);
                _stepResults[step.ToolName] = result;
                lastOutput = result;

                _log($"   ✅ Результат: {result.Length} символов");
            }

            var finalResult = lastOutput ?? string.Empty;
            stopwatch.Stop();

            _log($"🏁 Пайплайн завершён за {stopwatch.Elapsed.TotalMilliseconds:F0}мс");

            return new PipelineResult(
                Success: true,
                Output: finalResult,
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

    /// <summary>
    /// Выполняет один шаг пайплайна.
    /// </summary>
    private async Task<string> ExecuteStepAsync(string toolName, Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var normalizedToolName = toolName.Trim().ToLowerInvariant();
        _log($"   Инструмент: '{normalizedToolName}'");

        return normalizedToolName switch
        {
            "search" => await _searchTool!.ExecuteAsync(parameters),
            "summarize" => await _summarizeTool!.ExecuteAsync(parameters),
            "savetofile" => _saveToFileTool!.Execute(parameters),
            _ => throw new InvalidOperationException($"Неизвестный инструмент: '{toolName}'")
        };
    }

    /// <summary>
    /// Стандартный пайплайн: search → summarize → saveToFile.
    /// </summary>
    public async Task<PipelineResult> RunStandardPipelineAsync(
        string outputPath,
        string summarizeMode = "stats",
        CancellationToken cancellationToken = default)
    {
        var steps = new[]
        {
            new PipelineStep(
                "search",
                new Dictionary<string, object?>()
            ),
            new PipelineStep(
                "summarize",
                new Dictionary<string, object?>
                {
                    ["mode"] = summarizeMode
                }
            ),
            new PipelineStep(
                "saveToFile",
                new Dictionary<string, object?>
                {
                    ["path"] = outputPath,
                    ["format"] = "text"
                }
            )
        };

        return await ExecuteAsync(steps, cancellationToken);
    }

    /// <summary>
    /// Пайплайн с LLM-суммаризацией.
    /// </summary>
    public async Task<PipelineResult> RunLlmPipelineAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var steps = new[]
        {
            new PipelineStep(
                "search",
                new Dictionary<string, object?>()
            ),
            new PipelineStep(
                "summarize",
                new Dictionary<string, object?>
                {
                    ["mode"] = "llm"
                }
            ),
            new PipelineStep(
                "saveToFile",
                new Dictionary<string, object?>
                {
                    ["path"] = outputPath,
                    ["format"] = "markdown"
                }
            )
        };

        return await ExecuteAsync(steps, cancellationToken);
    }

    /// <summary>
    /// Пайплайн только для поиска и сохранения.
    /// </summary>
    public async Task<PipelineResult> RunSearchSavePipelineAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var steps = new[]
        {
            new PipelineStep(
                "search",
                new Dictionary<string, object?>()
            ),
            new PipelineStep(
                "saveToFile",
                new Dictionary<string, object?>
                {
                    ["path"] = outputPath,
                    ["format"] = "json"
                }
            )
        };

        return await ExecuteAsync(steps, cancellationToken);
    }

    /// <summary>
    /// Получает результаты всех шагов.
    /// </summary>
    public Dictionary<string, string> GetStepResults() => new(_stepResults);

    public void Dispose()
    {
        if (!_disposed)
        {
            (_searchTool as IDisposable)?.Dispose();
            (_summarizeTool as IDisposable)?.Dispose();
            _disposed = true;
        }
    }
}
