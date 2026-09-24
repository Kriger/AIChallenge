using System.Text.Json;

namespace AIChallenge.McpScheduler;

/// <summary>
/// Снимок состояния задач в момент времени.
/// </summary>
public record TaskSnapshot(
    DateTime Timestamp,
    int TotalCount,
    int CompletedCount,
    int PendingCount,
    Dictionary<string, int> ByProject,
    Dictionary<string, int> ByPriority,
    List<string> CompletedTaskTitles,
    List<string> PendingTaskTitles);

/// <summary>
/// Разница между двумя снимками.
/// </summary>
public record SnapshotDiff(
    int TotalBefore,
    int TotalAfter,
    int CompletedBefore,
    int CompletedAfter,
    int NewTasks,
    int NewlyCompleted,
    int NewlyPending,
    List<string> NewTaskTitles,
    List<string> NewlyCompletedTitles,
    List<string> NewlyPendingTitles);

/// <summary>
/// Хранилище снимков в JSON-файле.
/// </summary>
public sealed class ScheduledSummaryStore
{
    private readonly string _filePath;
    private List<TaskSnapshot> _snapshots = new();
    private string? _lastLlmSummary;
    private readonly string _llmSummaryPath;

    public ScheduledSummaryStore(string baseDirectory, string toolName = "scheduled_summary")
    {
        var memoryDir = Path.Combine(baseDirectory, "memory");
        if (!Directory.Exists(memoryDir))
            Directory.CreateDirectory(memoryDir);

        _filePath = Path.Combine(memoryDir, $"{toolName}_snapshots.json");
        _llmSummaryPath = Path.Combine(memoryDir, $"{toolName}_last_llm_summary.json");
        Load();
        LoadLlmSummary();
    }

    /// <summary>
    /// Сохраняет снимок.
    /// </summary>
    public void SaveSnapshot(TaskSnapshot snapshot)
    {
        _snapshots.Add(snapshot);
        Persist();
    }

    /// <summary>
    /// Загружает последний снимок.
    /// </summary>
    public TaskSnapshot? GetLatest()
    {
        return _snapshots.LastOrDefault();
    }

    /// <summary>
    /// Загружает снимок по индексу.
    /// </summary>
    public TaskSnapshot? GetAt(int index)
    {
        return index >= 0 && index < _snapshots.Count ? _snapshots[index] : null;
    }

    public string? GetLastLlmSummary() => _lastLlmSummary;

    public void SaveLlmSummary(string summary)
    {
        _lastLlmSummary = summary;
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(_llmSummaryPath, JsonSerializer.Serialize(new { summary }, options));
        }
        catch { /* ignore */ }
    }

    /// <summary>
    /// Вычисляет разницу между двумя снимками.
    /// </summary>
    public static SnapshotDiff ComputeDiff(TaskSnapshot? before, TaskSnapshot after)
    {
        if (before is null)
        {
            return new SnapshotDiff(
                TotalBefore: 0,
                TotalAfter: after.TotalCount,
                CompletedBefore: 0,
                CompletedAfter: after.CompletedCount,
                NewTasks: after.TotalCount,
                NewlyCompleted: after.CompletedCount,
                NewlyPending: after.PendingCount,
                NewTaskTitles: after.PendingTaskTitles.Concat(after.CompletedTaskTitles).ToList(),
                NewlyCompletedTitles: after.CompletedTaskTitles,
                NewlyPendingTitles: after.PendingTaskTitles
            );
        }

        var newTasks = after.PendingCount + after.CompletedCount - before.TotalCount;
        var newlyCompleted = after.CompletedCount - before.CompletedCount;
        var newlyPending = after.PendingCount - before.PendingCount;

        var newTaskTitles = after.PendingTaskTitles.Concat(after.CompletedTaskTitles)
            .Except(before.PendingTaskTitles.Concat(before.CompletedTaskTitles))
            .ToList();

        var newlyCompletedTitles = after.CompletedTaskTitles
            .Except(before.CompletedTaskTitles)
            .ToList();

        var newlyPendingTitles = after.PendingTaskTitles
            .Except(before.PendingTaskTitles)
            .ToList();

        return new SnapshotDiff(
            TotalBefore: before.TotalCount,
            TotalAfter: after.TotalCount,
            CompletedBefore: before.CompletedCount,
            CompletedAfter: after.CompletedCount,
            NewTasks: Math.Max(0, newTasks),
            NewlyCompleted: Math.Max(0, newlyCompleted),
            NewlyPending: Math.Max(0, newlyPending),
            NewTaskTitles: newTaskTitles,
            NewlyCompletedTitles: newlyCompletedTitles,
            NewlyPendingTitles: newlyPendingTitles
        );
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            _snapshots = JsonSerializer.Deserialize<List<TaskSnapshot>>(json, options) ?? new();
        }
        catch (Exception)
        {
            _snapshots.Clear();
        }
    }

    private void LoadLlmSummary()
    {
        if (!File.Exists(_llmSummaryPath))
            return;

        try
        {
            var json = File.ReadAllText(_llmSummaryPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("summary", out var s))
                _lastLlmSummary = s.GetString();
        }
        catch { /* ignore */ }
    }

    private void Persist()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(_snapshots, options);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Ошибка сохранения снимков: {ex.Message}");
        }
    }
}
