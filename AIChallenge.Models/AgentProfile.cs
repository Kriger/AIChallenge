using System.Text;

namespace AIChallenge.Models;

/// <summary>
/// Стиль общения агента.
/// </summary>
public enum CommunicationStyle
{
    /// <summary>Кратко и по делу, без вступлений.</summary>
    Concise,

    /// <summary>Развёрнуто, с пояснениями и контекстом.</summary>
    Detailed,

    /// <summary>Сбалансированный стиль.</summary>
    Balanced,
}

/// <summary>
/// Формат ответов агента.
/// </summary>
public enum OutputFormat
{
    /// <summary>Свободный текст.</summary>
    PlainText,

    /// <summary>Markdown с заголовками, списками, кодом.</summary>
    Markdown,

    /// <summary>Только код, без пояснений.</summary>
    CodeOnly,

    /// <summary>Структурированный: заголовок → суть → детали.</summary>
    Structured,
}

/// <summary>
/// Уровень глубины ответов агента.
/// Влияет на то, насколько подробно агент объясняет.
/// </summary>
public enum ExpertiseLevel
{
    /// <summary>Поверхностный — только основы, без углубления.</summary>
    Beginner,

    /// <summary>Средний — баланс между простотой и глубиной.</summary>
    Intermediate,

    /// <summary>Глубокий — полный разбор, все нюансы, профессиональная терминология.</summary>
    Expert,
}

/// <summary>
/// Язык ответов агента.
/// </summary>
public enum ResponseLanguage
{
    /// <summary>Русский.</summary>
    Russian,

    /// <summary>Английский.</summary>
    English,

    /// <summary>Авто — как пользователь, так и ассистент.</summary>
    Auto,
}

/// <summary>
/// Профиль агента — определяет роль, стиль, домен, предпочтения и ограничения.
/// Используется для персонализации поведения агента при каждом запросе.
/// </summary>
public class AgentProfile
{
    /// <summary>
    /// Идентификатор профиля. По умолчанию — "default".
    /// </summary>
    public string Id { get; set; } = "default";

    /// <summary>
    /// Имя агента. Отображается в интерфейсе и используется в системном сообщении.
    /// </summary>
    public string Name { get; set; } = "Агент";

    /// <summary>
    /// Роль агента — краткое описание, кто он.
    /// Например: "Архитектор .NET-решений", "Аналитик данных".
    /// </summary>
    public string Role { get; set; } = string.Empty;

    // === Стиль общения ===

    /// <summary>
    /// Стиль общения агента: кратко / развёрнуто / сбалансированно.
    /// По умолчанию — Balanced.
    /// </summary>
    public CommunicationStyle Style { get; set; } = CommunicationStyle.Balanced;

    /// <summary>
    /// Формат ответов: текст / markdown / только код / структурировано.
    /// По умолчанию — Markdown.
    /// </summary>
    public OutputFormat Format { get; set; } = OutputFormat.Markdown;

    /// <summary>
    /// Язык ответов.
    /// По умолчанию — Auto.
    /// </summary>
    public ResponseLanguage Language { get; set; } = ResponseLanguage.Auto;

    // === Уровень глубины ===

    /// <summary>
    /// Уровень глубины ответов агента.
    /// По умолчанию — Intermediate.
    /// </summary>
    public ExpertiseLevel Depth { get; set; } = ExpertiseLevel.Intermediate;

    // === Доменная область ===

    /// <summary>
    /// Доменная область агента (например: ".NET", "data-science", "DevOps").
    /// Агент специализируется в этой области.
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Предпочтительные технологии агента.
    /// Агент отдаёт приоритет этим технологиям при предложении решений.
    /// </summary>
    public List<string> PreferredTechnologies { get; set; } = new();

    /// <summary>
    /// Избегаемые технологии.
    /// Агент НЕ предлагает их без прямого запроса.
    /// </summary>
    public List<string> AvoidedTechnologies { get; set; } = new();

    // === Ограничения ===

    /// <summary>
    /// Максимальная длина ответа в словах. 0 = без ограничения.
    /// </summary>
    public int MaxResponseLength { get; set; } = 0;

    /// <summary>
    /// Запрещённые паттерны в ответах агента.
    /// Например: "не приводи примеры кода", "не используй MongoDB".
    /// </summary>
    public List<string> ResponseConstraints { get; set; } = new();

    /// <summary>
    /// Обязательные элементы в ответах агента.
    /// Например: "всегда указывай ссылки на документацию", "включай примеры".
    /// </summary>
    public List<string> ResponseRequirements { get; set; } = new();

    // === Произвольные инструкции ===

    /// <summary>
    /// Дополнительные инструкции для агента — свободноформатные правила поведения.
    /// </summary>
    public string Instructions { get; set; } = string.Empty;

    // === Инварианты ===

    /// <summary>
    /// Инварианты — непреложные правила, которые агент НЕ имеет права нарушать.
    /// Включают: выбранную архитектуру, принятые технические решения,
    /// ограничения по стеку, бизнес-правила.
    /// При конфликте запроса с инвариантом агент обязан отказать и объяснить почему.
    /// </summary>
    public List<string> Invariants { get; set; } = new();

    /// <summary>
    /// Время создания профиля.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Время последнего обновления профиля.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Сформированное системное сообщение на основе профиля агента.
    /// Генерируется динамически и включается в каждый запрос к LLM.
    /// </summary>
    public string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== ПРОФИЛЬ АГЕНТА ===");
        sb.AppendLine();

        // Имя и роль
        sb.AppendLine($"Ты — {Name}");
        if (!string.IsNullOrWhiteSpace(Role))
            sb.AppendLine($"Твоя роль: {Role}");
        sb.AppendLine();

        // Стиль
        var styleDesc = Style switch
        {
            CommunicationStyle.Concise => "СТИЛЬ: Отвечай КРАТКО и по делу. Без вступлений, воды и общих фраз. Только суть.",
            CommunicationStyle.Detailed => "СТИЛЬ: Отвечай развёрнуто, с пояснениями и контекстом. Ценится полнота ответа.",
            CommunicationStyle.Balanced => "СТИЛЬ: Отвечай сбалансированно — достаточно подробно, но без излишеств.",
            _ => "СТИЛЬ: Отвечай сбалансированно.",
        };
        sb.AppendLine(styleDesc);
        sb.AppendLine();

        // Формат
        var formatDesc = Format switch
        {
            OutputFormat.Markdown => "ФОРМАТ: Используй Markdown — заголовки, списки, блоки кода, жирный текст для ключевых терминов.",
            OutputFormat.PlainText => "ФОРМАТ: Отвечай свободным текстом, без форматирования.",
            OutputFormat.CodeOnly => "ФОРМАТ: Приводи только код, без пояснений и комментариев.",
            OutputFormat.Structured => "ФОРМАТ: Используй структуру — заголовок → краткая суть → подробности → выводы.",
            _ => "ФОРМАТ: Используй Markdown.",
        };
        sb.AppendLine(formatDesc);
        sb.AppendLine();

        // Язык
        var langDesc = Language switch
        {
            ResponseLanguage.Russian => "ЯЗЫК: Все ответы — на русском языке.",
            ResponseLanguage.English => "ЯЗЫК: All responses must be in English.",
            ResponseLanguage.Auto => "ЯЗЫК: Отвечай на том же языке, на котором написал пользователь.",
            _ => "ЯЗЫК: Отвечай на русском языке.",
        };
        sb.AppendLine(langDesc);
        sb.AppendLine();

        // Глубина
        var depthDesc = Depth switch
        {
            ExpertiseLevel.Beginner => "ГЛУБИНА: Поверхностный уровень — только основы, без углубления в детали.",
            ExpertiseLevel.Intermediate => "ГЛУБИНА: Средний уровень — баланс между простотой и глубиной. Используй профессиональную терминологию, но объясняй сложные моменты.",
            ExpertiseLevel.Expert => "ГЛУБИНА: Глубокий уровень — полный разбор, все нюансы. Используй профессиональную терминологию без объяснений базовых концепций.",
            _ => "ГЛУБИНА: Средний уровень.",
        };
        sb.AppendLine(depthDesc);
        sb.AppendLine();

        // Домен
        if (!string.IsNullOrWhiteSpace(Domain))
        {
            sb.AppendLine($"ДОМЕН: Ты специализируешься в области \"{Domain}\". Учитывай это при формировании ответов.");
            sb.AppendLine();
        }

        // Предпочтительные технологии
        if (PreferredTechnologies.Count > 0)
        {
            sb.AppendLine($"ПРЕДПОЧТИТЕЛЬНЫЕ ТЕХНОЛОГИИ: {string.Join(", ", PreferredTechnologies)}.");
            sb.AppendLine("Отдавай приоритет этим технологиям при предложении решений.");
            sb.AppendLine();
        }

        // Избегаемые технологии
        if (AvoidedTechnologies.Count > 0)
        {
            sb.AppendLine($"НЕ используй эти технологии (без прямого запроса): {string.Join(", ", AvoidedTechnologies)}.");
            sb.AppendLine();
        }

        // Ограничения
        if (MaxResponseLength > 0)
        {
            sb.AppendLine($"ОГРАНИЧЕНИЕ: Ответ не длиннее {MaxResponseLength} слов.");
            sb.AppendLine();
        }

        if (ResponseConstraints.Count > 0)
        {
            sb.AppendLine("ОБЯЗАТЕЛЬНЫЕ ОГРАНИЧЕНИЯ в ответах:");
            foreach (var constraint in ResponseConstraints)
            {
                sb.AppendLine($"  - {constraint}");
            }
            sb.AppendLine();
        }

        if (ResponseRequirements.Count > 0)
        {
            sb.AppendLine("ОБЯЗАТЕЛЬНЫЕ элементы в ответах:");
            foreach (var req in ResponseRequirements)
            {
                sb.AppendLine($"  - {req}");
            }
            sb.AppendLine();
        }

        // Инструкции
        if (!string.IsNullOrWhiteSpace(Instructions))
        {
            sb.AppendLine("ДОПОЛНИТЕЛЬНЫЕ ИНСТРУКЦИИ:");
            sb.AppendLine(Instructions);
            sb.AppendLine();
        }

        sb.AppendLine("=== КОНЕЦ ПРОФИЛЯ АГЕНТА ===");

        return sb.ToString();
    }
}
