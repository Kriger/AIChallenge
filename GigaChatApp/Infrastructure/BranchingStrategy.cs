using GigaChatApp.Models;
using GigaChatApp.Services;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Стратегия 3: Branching — ветвление диалога.
/// Позволяет сохранять checkpoint и создавать ветки от одного места.
/// Каждая ветка развивается независимо.
/// </summary>
public class BranchingStrategy : IContextStrategy
{
    private readonly List<DialogueBranch> _branches = new();
    private readonly List<BranchCheckpoint> _checkpoints = new();
    private int _branchCounter = 0;
    private int _checkpointCounter = 0;

    /// <summary>Идентификатор текущей активной ветки.</summary>
    public string ActiveBranchId { get; set; } = string.Empty;

    /// <summary>Общее количество созданных веток.</summary>
    public int BranchCount => _branches.Count;

    /// <summary>Общее количество чекпоинтов.</summary>
    public int CheckpointCount => _checkpoints.Count;

    public string Name => "Branching";

    public string Description => $"Ветвление диалога. Активная ветка: {ActiveBranchName ?? "(нет)"}. Всего веток: {_branches.Count}, чекпоинтов: {_checkpoints.Count}.";

    /// <summary>
    /// Имя текущей активной ветки.
    /// </summary>
    public string? ActiveBranchName => _branches.FirstOrDefault(b => b.Id == ActiveBranchId)?.Name;

    public BranchingStrategy()
    {
        // Создаём основную ветку
        var mainBranch = new DialogueBranch
        {
            Id = "main",
            Name = "main",
            IsMain = true,
            CreatedAt = DateTime.UtcNow,
        };
        _branches.Add(mainBranch);
        ActiveBranchId = "main";
    }

    /// <summary>
    /// Получить текущую активную ветку.
    /// </summary>
    private DialogueBranch ActiveBranch =>
        _branches.FirstOrDefault(b => b.Id == ActiveBranchId)
        ?? throw new InvalidOperationException($"Ветка '{ActiveBranchId}' не найдена");

    public void AddMessage(ApiMessage message)
    {
        var branch = ActiveBranch;
        branch.Messages.Add(message);
        branch.LastModified = DateTime.UtcNow;
    }

    /// <summary>
    /// Создать checkpoint в текущей ветке.
    /// </summary>
    /// <param name="name">Название чекпоинта.</param>
    /// <returns>Созданный checkpoint.</returns>
    public BranchCheckpoint CreateCheckpoint(string name)
    {
        var branch = ActiveBranch;
        var checkpoint = new BranchCheckpoint
        {
            Id = $"cp-{++_checkpointCounter}",
            Name = name,
            MessageIndex = branch.Messages.Count,
            BranchId = branch.Id,
            Facts = new Dictionary<string, string>(branch.Facts, StringComparer.OrdinalIgnoreCase),
            MessageCount = branch.Messages.Count,
            CreatedAt = DateTime.UtcNow,
        };

        _checkpoints.Add(checkpoint);
        return checkpoint;
    }

    /// <summary>
    /// Создать новую ветку от checkpoint.
    /// </summary>
    /// <param name="checkpointId">Идентификатор чекпоинта.</param>
    /// <param name="branchName">Название новой ветки.</param>
    /// <returns>Идентификатор новой ветки.</returns>
    public string CreateBranchFromCheckpoint(string checkpointId, string branchName)
    {
        var checkpoint = _checkpoints.FirstOrDefault(cp => cp.Id == checkpointId);
        if (checkpoint is null)
            throw new InvalidOperationException($"Checkpoint '{checkpointId}' не найден");

        var sourceBranch = _branches.FirstOrDefault(b => b.Id == checkpoint.BranchId);
        if (sourceBranch is null)
            throw new InvalidOperationException($"Ветка '{checkpoint.BranchId}' не найдена");

        // Копируем сообщения и факты на момент checkpoint
        var newMessages = sourceBranch.Messages.Take(checkpoint.MessageIndex).ToList();
        var newFacts = new Dictionary<string, string>(checkpoint.Facts, StringComparer.OrdinalIgnoreCase);

        var newBranch = new DialogueBranch
        {
            Id = $"branch-{++_branchCounter}",
            Name = branchName,
            Messages = newMessages,
            Facts = newFacts,
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.UtcNow,
            IsMain = false,
        };

        _branches.Add(newBranch);
        return newBranch.Id;
    }

    /// <summary>
    /// Создать новую ветку от текущей точки.
    /// </summary>
    /// <param name="branchName">Название новой ветки.</param>
    /// <returns>Идентификатор новой ветки.</returns>
    public string CreateBranch(string branchName)
    {
        var branch = ActiveBranch;
        var newBranch = branch.Clone($"branch-{++_branchCounter}", branchName);
        _branches.Add(newBranch);
        return newBranch.Id;
    }

    /// <summary>
    /// Переключиться на другую ветку.
    /// </summary>
    /// <param name="branchId">Идентификатор ветки.</param>
    public void SwitchBranch(string branchId)
    {
        if (!_branches.Any(b => b.Id == branchId))
            throw new InvalidOperationException($"Ветка '{branchId}' не найдена");

        ActiveBranchId = branchId;
    }

    // ==================== Факты ветки ====================

    /// <summary>
    /// Факты текущей активной ветки.
    /// </summary>
    public IReadOnlyDictionary<string, string> Facts => ActiveBranch.Facts;

    /// <summary>
    /// Сохранить факт в текущей ветке.
    /// </summary>
    public void SaveFact(string key, string value)
    {
        ActiveBranch.Facts[key] = value;
        ActiveBranch.LastModified = DateTime.UtcNow;
    }

    /// <summary>
    /// Удалить факт из текущей ветки.
    /// </summary>
    public bool DeleteFact(string key)
    {
        var removed = ActiveBranch.Facts.Remove(key);
        if (removed)
            ActiveBranch.LastModified = DateTime.UtcNow;
        return removed;
    }

    /// <summary>
    /// Получить факт по ключу.
    /// </summary>
    public string? GetFact(string key)
    {
        return ActiveBranch.Facts.GetValueOrDefault(key);
    }

    /// <summary>
    /// Получить все факты всех веток.
    /// </summary>
    public IReadOnlyList<DialogueBranch> GetAllBranches() => _branches.AsReadOnly();

    /// <summary>
    /// Удалить ветку (кроме main).
    /// </summary>
    public void DeleteBranch(string branchId)
    {
        var branch = _branches.FirstOrDefault(b => b.Id == branchId);
        if (branch is null)
            throw new InvalidOperationException($"Ветка '{branchId}' не найдена");

        if (branch.IsMain)
            throw new InvalidOperationException("Нельзя удалить основную ветку");

        _branches.Remove(branch);

        // Если удалили активную ветку — переключаемся на main
        if (ActiveBranchId == branchId)
        {
            ActiveBranchId = "main";
        }
    }

    public ContextResult BuildContext()
    {
        var branch = ActiveBranch;
        var originalTokens = TokenEstimator.EstimateHistoryTokens(branch.Messages);
        var processedTokens = originalTokens;

        // Формируем факты ветки
        var factsText = string.Empty;
        if (branch.Facts.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== ФАКТЫ ВЕТКИ \"{branch.Name}\" ===");
            foreach (var kvp in branch.Facts)
            {
                sb.AppendLine($"{kvp.Key}: {kvp.Value}");
            }
            sb.AppendLine($"=== КОНЕЦ ФАКТОВ ВЕТКИ \"{branch.Name}\" ===");
            factsText = sb.ToString();
            processedTokens += TokenEstimator.EstimateTokens(factsText);
        }

        // Добавляем информацию о ветке и факты в system-сообщение
        var systemMessages = new List<ApiMessage>();

        if (!string.IsNullOrEmpty(factsText))
        {
            systemMessages.Add(new ApiMessage
            {
                Role = "system",
                Content = factsText,
            });
        }

        // Информация о ветвлении — только если есть другие ветки
        if (BranchCount > 1)
        {
            var branchInfo = $"[Ветка: \"{branch.Name}\" ({branch.Id}) — АКТИВНАЯ]\n" +
                             $"Других веток: {BranchCount - 1}";
            systemMessages.Add(new ApiMessage
            {
                Role = "system",
                Content = $"=== ВЕТВЛЕНИЕ ===\n{branchInfo}\n=================",
            });
        }

        return new ContextResult
        {
            Messages = branch.Messages.ToList(),
            SystemMessages = systemMessages,
            SummaryText = $"Ветка: {branch.Name} ({branch.Messages.Count} сообщений, {branch.Facts.Count} фактов)",
            OriginalTokens = originalTokens,
            CompressedTokens = processedTokens,
            Description = $"Branching: ветка \"{branch.Name}\", {branch.Messages.Count} сообщений, {branch.Facts.Count} фактов",
            IsCompressed = false,
        };
    }

    public void Clear()
    {
        _branches.Clear();
        _checkpoints.Clear();
        _branchCounter = 0;
        _checkpointCounter = 0;

        var mainBranch = new DialogueBranch
        {
            Id = "main",
            Name = "main",
            IsMain = true,
            CreatedAt = DateTime.UtcNow,
        };
        _branches.Add(mainBranch);
        ActiveBranchId = "main";
    }

    public void LoadHistory(IEnumerable<ApiMessage> messages)
    {
        var branch = ActiveBranch;
        branch.Messages.Clear();
        foreach (var msg in messages)
        {
            branch.Messages.Add(msg);
        }
        branch.LastModified = DateTime.UtcNow;
    }

    /// <summary>
    /// Получить все чекпоинты.
    /// </summary>
    public IReadOnlyList<BranchCheckpoint> GetAllCheckpoints() => _checkpoints.AsReadOnly();

    /// <summary>
    /// Внутренний метод для восстановления из сохранения (доступен через рефлексию).
    /// </summary>
    internal void AddBranchInternal(DialogueBranch branch) => _branches.Add(branch);
    internal void AddCheckpointInternal(BranchCheckpoint checkpoint) => _checkpoints.Add(checkpoint);

    public string GetStatus()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Стратегия: {Name}");
        sb.AppendLine($"Активная ветка: {ActiveBranchName ?? "(нет)"} ({ActiveBranchId})");
        sb.AppendLine($"Всего веток: {BranchCount}");
        sb.AppendLine($"Чекпоинтов: {CheckpointCount}");
        sb.AppendLine();

        foreach (var branch in _branches)
        {
            var marker = branch.Id == ActiveBranchId ? " ▶" : "  ";
            sb.AppendLine($"  {marker} [{branch.Id}] \"{branch.Name}\" — {branch.Messages.Count} сообщений, {branch.Facts.Count} фактов{(branch.IsMain ? " (main)" : "")}");
        }

        // Факты активной ветки
        var active = ActiveBranch;
        if (active.Facts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Факты текущей ветки:");
            foreach (var kvp in active.Facts)
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
        }

        if (_checkpoints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Чекпоинты:");
            foreach (var cp in _checkpoints)
            {
                sb.AppendLine($"  [{cp.Id}] \"{cp.Name}\" — ветка {cp.BranchId}, msg#{cp.MessageIndex}");
            }
        }

        return sb.ToString();
    }
}
