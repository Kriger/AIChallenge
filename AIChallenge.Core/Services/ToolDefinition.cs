namespace AIChallenge.Core.Services;

/// <summary>
/// Определение инструмента для function calling.
/// </summary>
public record ToolDefinition(
    string Name,
    string Description,
    object Parameters);
