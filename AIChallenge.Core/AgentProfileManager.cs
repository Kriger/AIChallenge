using System.Text.Json;
using System.Text.Json.Serialization;
using AIChallenge.Models;

namespace AIChallenge.Core.Infrastructure;

/// <summary>
/// Сериализуемая версия профиля агента для JSON.
/// </summary>
internal class AgentProfileDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("style")]
    public string Style { get; set; } = string.Empty;

    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("depth")]
    public string Depth { get; set; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("preferredTechnologies")]
    public List<string> PreferredTechnologies { get; set; } = new();

    [JsonPropertyName("avoidedTechnologies")]
    public List<string> AvoidedTechnologies { get; set; } = new();

    [JsonPropertyName("maxResponseLength")]
    public int MaxResponseLength { get; set; }

    [JsonPropertyName("responseConstraints")]
    public List<string> ResponseConstraints { get; set; } = new();

    [JsonPropertyName("responseRequirements")]
    public List<string> ResponseRequirements { get; set; } = new();

    [JsonPropertyName("instructions")]
    public string Instructions { get; set; } = string.Empty;

    [JsonPropertyName("invariants")]
    public List<string> Invariants { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Менеджер профилей агентов.
/// Загружает, сохраняет и управляет профилями из profiles/agent_profile.json.
/// </summary>
public static class AgentProfileManager
{
    private static string ProfilesDir => Path.Combine(AppContext.BaseDirectory, "profiles");
    private const string FileName = "agent_profile.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string DirectoryPath => Path.GetFullPath(ProfilesDir);

    public static string FilePath => Path.GetFullPath(Path.Combine(ProfilesDir, FileName));

    /// <summary>
    /// Гарантирует, что директория profiles существует.
    /// </summary>
    private static void EnsureDirectory()
    {
        if (!Directory.Exists(DirectoryPath))
            Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>
    /// Загружает профиль агента из файла. Если файл не найден — возвращает профиль по умолчанию.
    /// </summary>
    public static AgentProfile Load(string profileId = "default")
    {
        if (!File.Exists(FilePath))
            return CreateDefaultProfile(profileId);

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var profiles = JsonSerializer.Deserialize<Dictionary<string, AgentProfileDto>>(json, Options);

            if (profiles is null || !profiles.TryGetValue(profileId, out var dto))
                return CreateDefaultProfile(profileId);

            return FromDto(dto);
        }
        catch
        {
            return CreateDefaultProfile(profileId);
        }
    }

    /// <summary>
    /// Сохраняет профиль агента в файл.
    /// </summary>
    public static void Save(AgentProfile profile)
    {
        profile.UpdatedAt = DateTime.UtcNow;
        EnsureDirectory();

        Dictionary<string, AgentProfileDto> profiles;
        if (File.Exists(FilePath))
        {
            try
            {
                var json = File.ReadAllText(FilePath, Encoding.UTF8);
                profiles = JsonSerializer.Deserialize<Dictionary<string, AgentProfileDto>>(json, Options)
                           ?? new Dictionary<string, AgentProfileDto>(StringComparer.OrdinalIgnoreCase);
                profiles[profile.Id] = ToDto(profile);
            }
            catch
            {
                profiles = new Dictionary<string, AgentProfileDto>(StringComparer.OrdinalIgnoreCase)
                {
                    [profile.Id] = ToDto(profile)
                };
            }
        }
        else
        {
            profiles = new Dictionary<string, AgentProfileDto>(StringComparer.OrdinalIgnoreCase)
            {
                [profile.Id] = ToDto(profile)
            };
            var json = JsonSerializer.Serialize(profiles, Options);
            File.WriteAllText(FilePath, json, Encoding.UTF8);
            return;
        }

        var jsonNew = JsonSerializer.Serialize(profiles, Options);
        File.WriteAllText(FilePath, jsonNew, Encoding.UTF8);
    }

    /// <summary>
    /// Создаёт профиль агента по умолчанию.
    /// </summary>
    private static AgentProfile CreateDefaultProfile(string profileId)
    {
        return new AgentProfile
        {
            Id = profileId,
            Name = "Агент",
            Role = string.Empty,
            Style = CommunicationStyle.Balanced,
            Format = OutputFormat.Markdown,
            Language = ResponseLanguage.Auto,
            Depth = ExpertiseLevel.Intermediate,
            Domain = string.Empty,
            PreferredTechnologies = new(),
            AvoidedTechnologies = new(),
            MaxResponseLength = 0,
            ResponseConstraints = new(),
            ResponseRequirements = new(),
            Instructions = string.Empty,
            Invariants = new(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Преобразует профиль в DTO для сериализации.
    /// </summary>
    private static AgentProfileDto ToDto(AgentProfile profile)
    {
        return new AgentProfileDto
        {
            Id = profile.Id,
            Name = profile.Name,
            Role = profile.Role,
            Style = profile.Style.ToString(),
            Format = profile.Format.ToString(),
            Language = profile.Language.ToString(),
            Depth = profile.Depth.ToString(),
            Domain = profile.Domain,
            PreferredTechnologies = profile.PreferredTechnologies,
            AvoidedTechnologies = profile.AvoidedTechnologies,
            MaxResponseLength = profile.MaxResponseLength,
            ResponseConstraints = profile.ResponseConstraints,
            ResponseRequirements = profile.ResponseRequirements,
            Instructions = profile.Instructions,
            Invariants = profile.Invariants,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt,
        };
    }

    /// <summary>
    /// Преобразует DTO в профиль.
    /// </summary>
    private static AgentProfile FromDto(AgentProfileDto dto)
    {
        return new AgentProfile
        {
            Id = dto.Id,
            Name = dto.Name,
            Role = dto.Role,
            Style = Enum.TryParse<CommunicationStyle>(dto.Style, ignoreCase: true, out var s) ? s : CommunicationStyle.Balanced,
            Format = Enum.TryParse<OutputFormat>(dto.Format, ignoreCase: true, out var f) ? f : OutputFormat.Markdown,
            Language = Enum.TryParse<ResponseLanguage>(dto.Language, ignoreCase: true, out var l) ? l : ResponseLanguage.Auto,
            Depth = Enum.TryParse<ExpertiseLevel>(dto.Depth, ignoreCase: true, out var d) ? d : ExpertiseLevel.Intermediate,
            Domain = dto.Domain,
            PreferredTechnologies = dto.PreferredTechnologies,
            AvoidedTechnologies = dto.AvoidedTechnologies,
            MaxResponseLength = dto.MaxResponseLength,
            ResponseConstraints = dto.ResponseConstraints,
            ResponseRequirements = dto.ResponseRequirements,
            Instructions = dto.Instructions,
            Invariants = dto.Invariants,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
        };
    }
}
