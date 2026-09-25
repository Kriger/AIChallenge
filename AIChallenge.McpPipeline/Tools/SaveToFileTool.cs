using System.Text;
using System.Text.Json;

namespace AIChallenge.McpPipeline.Tools;

/// <summary>
/// Инструмент saveToFile — сохранение результата в файл.
/// Принимает контент и путь, сохраняет с форматированием.
/// </summary>
public sealed class SaveToFileTool
{
    private readonly Action<string> _log;

    public string Name => "saveToFile";
    public string Description => "Сохранение результата в файл. Поддерживает форматы: json, text, markdown.";

    public SaveToFileTool(Action<string>? log = null)
    {
        _log = log ?? (msg => Console.WriteLine($"  [saveToFile] {msg}"));
    }

    /// <summary>
    /// Сохраняет контент в файл.
    /// Параметры: content (string), path (string), format (string? = "text")
    /// Возвращает: результат сохранения (string).
    /// </summary>
    public string Execute(Dictionary<string, object?> parameters)
    {
        // Получаем параметры
        if (!parameters.TryGetValue("content", out var contentObj) || contentObj == null)
        {
            return "{\"error\": \"Отсутствует параметр content\"}";
        }

        if (!parameters.TryGetValue("path", out var pathObj) || pathObj == null)
        {
            return "{\"error\": \"Отсутствует параметр path\"}";
        }

        var content = contentObj.ToString() ?? string.Empty;
        var path = pathObj.ToString() ?? string.Empty;
        var format = parameters.TryGetValue("format", out var formatObj)
            ? (formatObj.ToString()?.ToLowerInvariant() ?? "text")
            : "text";

        _log($"💾 Сохранение в: {path} (формат: {format})");

        // Проверяем путь
        if (string.IsNullOrWhiteSpace(path))
        {
            return "{\"error\": \"Путь не может быть пустым\"}";
        }

        try
        {
            // Создаём директорию, если не существует
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _log($"   Создана директория: {directory}");
            }

            // Форматируем контент
            string formattedContent;
            switch (format.ToLowerInvariant())
            {
                case "json":
                    formattedContent = FormatJson(content);
                    break;

                case "markdown":
                    formattedContent = FormatMarkdown(content);
                    break;

                case "text":
                default:
                    formattedContent = content;
                    break;
            }

            // Сохраняем файл
            File.WriteAllText(path, formattedContent, Encoding.UTF8);

            var fileInfo = new FileInfo(path);
            var result = new
            {
                success = true,
                path = path,
                size = fileInfo.Length,
                format = format,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var resultJson = JsonSerializer.Serialize(result, options);

            _log($"✅ Сохранено: {fileInfo.Length} байт");
            return resultJson;
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка сохранения: {ex.Message}");
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }

    /// <summary>
    /// Форматирует JSON-строку с отступами.
    /// </summary>
    private static string FormatJson(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            return doc.RootElement.GetRawText();
        }
        catch
        {
            // Если не JSON — возвращаем как есть
            return content;
        }
    }

    /// <summary>
    /// Форматирует контент как Markdown.
    /// </summary>
    private static string FormatMarkdown(string content)
    {
        // Добавляем блок кода, если контент не является markdown
        if (!content.Trim().StartsWith("#") && !content.Trim().StartsWith("```"))
        {
            return $"```\n{content}\n```";
        }
        return content;
    }
}
