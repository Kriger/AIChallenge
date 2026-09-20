using System.Text.Json.Serialization;

namespace GigaChatApp.Models;

/// <summary>
/// Результат проверки допустимости перехода между состояниями.
/// </summary>
public record TransitionResult(
    [property: JsonPropertyName("allowed")] bool Allowed,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("current_stage")] string CurrentStage,
    [property: JsonPropertyName("target_stage")] string TargetStage,
    [property: JsonPropertyName("allowed_next")] IReadOnlyList<string> AllowedNext,
    [property: JsonPropertyName("missing_conditions")] IReadOnlyList<string>? MissingConditions);
