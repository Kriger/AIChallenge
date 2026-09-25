using System.Text.Json;

namespace AIChallenge.McpPipeline.Tools;

/// <summary>
/// Общие утилиты для работы с JSON.
/// </summary>
public static class JsonUtils
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Извлекает строковое значение из JsonElement по одному из возможных имён свойства.
    /// </summary>
    public static string? ExtractString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                return prop.ValueKind switch
                {
                    JsonValueKind.String => prop.GetString(),
                    JsonValueKind.Number => prop.GetInt32().ToString(),
                    JsonValueKind.True or JsonValueKind.False => prop.GetBoolean().ToString().ToLowerInvariant(),
                    JsonValueKind.Null => null,
                    _ => prop.ToString()
                };
            }
        }
        return null;
    }

    /// <summary>
    /// Извлекает строковое значение из Dictionary<string, JsonElement>.
    /// </summary>
    public static string? ExtractString(Dictionary<string, JsonElement> dict, params string[] names)
    {
        foreach (var name in names)
        {
            if (dict.TryGetValue(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    /// <summary>
    /// Форматирует JSON-строку с отступами.
    /// </summary>
    public static string FormatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, IndentedOptions);
        }
        catch
        {
            return json;
        }
    }

    /// <summary>
    /// Сериализует объект в красиво отформатированный JSON.
    /// </summary>
    public static string Serialize(object obj)
    {
        return JsonSerializer.Serialize(obj, IndentedOptions);
    }

    /// <summary>
    /// Считает количество элементов в JSON-массиве.
    /// </summary>
    public static int CountArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.GetArrayLength()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Собирает Dictionary<string, JsonElement> обратно в JSON-строку.
    /// </summary>
    public static string ToJsonString(Dictionary<string, JsonElement> dict)
    {
        var parts = new string[dict.Count];
        int i = 0;
        foreach (var kvp in dict)
        {
            var escapedKey = EscapeJsonKey(kvp.Key);
            parts[i++] = $"\"{escapedKey}\":{kvp.Value.GetRawText()}";
        }
        return "{" + string.Join(",", parts) + "}";
    }

    private static string EscapeJsonKey(string key)
    {
        return key.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Экранирует строку для вставки в JSON.
    /// </summary>
    public static string EscapeJsonString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
