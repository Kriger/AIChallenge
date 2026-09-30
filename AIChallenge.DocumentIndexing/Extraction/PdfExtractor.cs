using UglyToad.PdfPig;

namespace AIChallenge.DocumentIndexing.Extraction;

/// <summary>
/// Извлекает текст из PDF-файлов с помощью PdfPig.
/// </summary>
public class PdfExtractor : IDocumentExtractor
{
    public string FormatName => "pdf";
    public string[] Extensions => new[] { ".pdf" };

    /// <summary>
    /// Извлекает текст из PDF-файла.
    /// Обрабатывает многостраничные документы, сохраняя порядок страниц.
    /// </summary>
    public async Task<string> ExtractAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}");

        var pages = new List<string>();

        using (var pdf = PdfDocument.Open(filePath))
        {
            var totalPages = pdf.NumberOfPages;

            for (var i = 1; i <= totalPages; i++)
            {
                var page = pdf.GetPage(i);
                var text = page.Text;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    pages.Add(text);
                }
            }
        }

        // Объединяем страницы с разделителем
        var separator = new string('-', 80) + "\n";
        return string.Join(separator, pages);
    }
}
