using System.Text;
using System.Text.Json;

namespace AIChallenge.McpPipeline.Tools;

/// <summary>
/// Инструмент saveToFile — сохранение результата в файл.
/// </summary>
public sealed class SaveToFileTool : IPipelineTool
{
    private readonly Action<string> _log;

    public string Summary { get; private set; } = "";
    public string Name => "saveToFile";
    public string Description => "Сохранение результата в файл. Поддерживает форматы: json, text, markdown.";

    public SaveToFileTool(Action<string>? log = null)
    {
        _log = log ?? (msg => Console.WriteLine($"  [saveToFile] {msg}"));
    }

    public Task<string> ExecuteAsync(Dictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("content", out var contentObj) || contentObj == null)
            return Task.FromResult("{\"error\": \"Отсутствует параметр content\"}");

        if (!parameters.TryGetValue("path", out var pathObj) || pathObj == null)
            return Task.FromResult("{\"error\": \"Отсутствует параметр path\"}");

        var content = contentObj.ToString() ?? string.Empty;
        var path = pathObj.ToString() ?? string.Empty;
        var format = parameters.TryGetValue("format", out var formatObj)
            ? (formatObj?.ToString()?.ToLowerInvariant() ?? "text")
            : "text";

        _log($"💾 Сохранение в: {path} (формат: {format})");

        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult("{\"error\": \"Путь не может быть пустым\"}");

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _log($"   Создана директория: {directory}");
            }

            var formattedContent = format switch
            {
                "json" => FormatJson(content),
                "markdown" => FormatMarkdown(content),
                _ => content
            };

            File.WriteAllText(path, formattedContent, Encoding.UTF8);

            var fileInfo = new FileInfo(path);
            var result = new { success = true, path, size = fileInfo.Length, format, timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
            Summary = $"Сохранено: {fileInfo.Length} байт";
            _log($"✅ {Summary}");

            return Task.FromResult(JsonUtils.Serialize(result));
        }
        catch (Exception ex)
        {
            _log($"❌ Ошибка сохранения: {ex.Message}");
            return Task.FromResult($"{{\"error\": \"{ex.Message}\"}}");
        }
    }

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
            return JsonSerializer.Serialize(doc.RootElement, options);
        }
        catch
        {
            return content;
        }
    }

    private static string FormatMarkdown(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("#") && !trimmed.StartsWith("```"))
            return $"```\n{content}\n```";
        return content;
    }
}
