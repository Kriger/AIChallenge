using System.Text.Json.Serialization;

namespace AIChallenge.Models;

/// <summary>
/// Артефакт, созданный на этапе выполнения.
/// </summary>
public record ArtifactEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("created_at")] string CreatedAt);
