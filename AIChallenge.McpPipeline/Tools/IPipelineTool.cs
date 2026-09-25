namespace AIChallenge.McpPipeline.Tools;

/// <summary>
/// Общий интерфейс для всех инструментов пайплайна.
/// </summary>
public interface IPipelineTool
{
    /// <summary>
    /// Уникальное имя инструмента.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Описание инструмента.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Краткая сводка результата выполнения (для отображения в консоли).
    /// </summary>
    string Summary { get; }

    /// <summary>
    /// Выполняет инструмент с заданными параметрами.
    /// </summary>
    /// <param name="parameters">Параметры инструмента.</param>
    /// <returns>Результат выполнения (для передачи следующему шагу).</returns>
    Task<string> ExecuteAsync(Dictionary<string, object?> parameters);
}
