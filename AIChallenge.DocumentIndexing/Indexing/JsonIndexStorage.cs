using AIChallenge.DocumentIndexing.Models;

namespace AIChallenge.DocumentIndexing.Indexing;

/// <summary>
/// Хранилище индекса в JSON формате.
/// Простое решение для небольших наборов данных.
/// </summary>
public class JsonIndexStorage : IIndexStorage
{
    public string Name => "json";

    /// <summary>
    /// Сохраняет индекс в JSON файл.
    /// </summary>
    public async Task SaveAsync(DocumentIndex index, float[][] embeddings, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        // Добавляем эмбеддинги в индекс
        for (var i = 0; i < index.Chunks.Count && i < embeddings.Length; i++)
        {
            index.Chunks[i].Embedding = embeddings[i];
        }

        // Сохраняем полный индекс с эмбеддингами
        var json = index.ToJson();
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Загружает индекс из JSON файла.
    /// </summary>
    public async Task<(DocumentIndex Index, float[][] Embeddings)> LoadAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8);
        var index = DocumentIndex.FromJson(json) ?? throw new InvalidOperationException("Failed to deserialize index");

        // Извлекаем эмбеддинги из чанков
        var embeddings = index.Chunks
            .Where(c => c.Embedding is not null)
            .Select(c => c.Embedding!)
            .ToArray();

        return (index, embeddings);
    }

    /// <summary>
    /// Выполняет поиск по косинусному сходству.
    /// </summary>
    public (int[] indices, float[] distances) Search(float[] queryEmbedding, int topK, float[][] embeddings)
    {
        var scores = new List<(int Index, float Score)>();

        for (var i = 0; i < embeddings.Length; i++)
        {
            var score = CosineSimilarity(queryEmbedding, embeddings[i]);
            scores.Add((i, score));
        }

        scores.Sort((a, b) => b.Score.CompareTo(a.Score));

        var k = Math.Min(topK, scores.Count);
        var indices = new int[k];
        var distances = new float[k];

        for (var i = 0; i < k; i++)
        {
            indices[i] = scores[i].Index;
            distances[i] = 1.0f - scores[i].Score;
        }

        return (indices, distances);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same dimension", nameof(b));

        var dotProduct = 0.0f;
        var normA = 0.0f;
        var normB = 0.0f;

        for (var i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
            return 0.0f;

        return dotProduct / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
