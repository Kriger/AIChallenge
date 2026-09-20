using System.Text.Json.Serialization;

namespace GigaChatApp.Models;

/// <summary>
/// Запись истории диалога: роль и текст.
/// </summary>
public record DialogEntry(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("text")] string Text);

/// <summary>
/// Запись об завершённом шаге в истории FSM.
/// </summary>
public record HistoryEntry(
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("step_number")] int StepNumber,
    [property: JsonPropertyName("step_description")] string StepDescription,
    [property: JsonPropertyName("completed")] bool Completed,
    [property: JsonPropertyName("completed_at")] string CompletedAt);

/// <summary>
/// Контекст этапа сбора требований.
/// </summary>
public record RequirementsContext(
    [property: JsonPropertyName("questions")] List<string> Questions,
    [property: JsonPropertyName("answers")] Dictionary<string, string> Answers,
    [property: JsonPropertyName("current_question_index")] int CurrentQuestionIndex,
    [property: JsonPropertyName("dialog_history")] List<DialogEntry> DialogHistory);

/// <summary>
/// Полное состояние конечного автомата задачи.
/// </summary>
public record TaskState(
    [property: JsonPropertyName("stage")] TaskStage Stage,
    [property: JsonPropertyName("step")] TaskStep Step,
    [property: JsonPropertyName("next_action")] string NextAction,
    [property: JsonPropertyName("history")] List<HistoryEntry> History,
    [property: JsonPropertyName("paused")] bool Paused,
    [property: JsonPropertyName("requirements_context")] RequirementsContext? RequirementsContext);
