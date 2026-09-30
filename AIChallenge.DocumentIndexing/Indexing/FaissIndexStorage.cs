using AIChallenge.DocumentIndexing.Models;

namespace AIChallenge.DocumentIndexing.Indexing;

/// <summary>
/// Хранилище индекса в формате FAISS (бинарный).
/// Использует упрощённый подход: сохраняет эмбеддинги в бинарном формате
/// и выполняет поиск по косинусному сходству.
/// </summary>
public class FaissIndexStorage : IIndexStorage
{
    public string Name => "faiss";

    private const int Version = 1;

    /// <summary>
    /// Сохраняет индекс в бинарный файл (упрощённый FAISS-совместимый формат).
    /// </summary>
    public async Task SaveAsync(DocumentIndex index, float[][] embeddings, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        // Сохраняем метаданные индекса в JSON
        var metadataPath = Path.ChangeExtension(path, ".json");
        index.ChunkingStrategies.Add("faiss");
        var json = index.ToJson();
        await File.WriteAllTextAsync(metadataPath, json, Encoding.UTF8);

        // Сохраняем эмбеддинги в бинарном формате (FAISS-like)
        // Формат: [version(4 bytes)][dimension(4 bytes)][n_items(4 bytes)][data(float32)]
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        // Заголовок
        bw.Write(Version);
        bw.Write(embeddings[0].Length); // dimension
        bw.Write(embeddings.Length); // n_items

        // Данные (row-major order)
        foreach (var embedding in embeddings)
        {
            foreach (var value in embedding)
            {
                bw.Write(value);
            }
        }

        bw.Flush();
    }

    /// <summary>
    /// Загружает индекс из бинарного файла.
    /// </summary>
    public async Task<(DocumentIndex Index, float[][] Embeddings)> LoadAsync(string path)
    {
        var metadataPath = Path.ChangeExtension(path, ".json");
        
        if (!File.Exists(metadataPath))
            throw new FileNotFoundException($"Metadata file not found: {metadataPath}");

        var json = await File.ReadAllTextAsync(metadataPath, Encoding.UTF8);
        var index = DocumentIndex.FromJson(json) ?? throw new InvalidOperationException("Failed to deserialize index");

        // Загружаем эмбеддинги из бинарного файла
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);

        var version = br.ReadInt32();
        if (version != Version)
            throw new InvalidOperationException($"Unsupported FAISS version: {version}");

        var dimension = br.ReadInt32();
        var itemCount = br.ReadInt32();

        var embeddings = new float[itemCount][];
        for (var i = 0; i < itemCount; i++)
        {
            embeddings[i] = new float[dimension];
            for (var j = 0; j < dimension; j++)
            {
                embeddings[i][j] = br.ReadSingle();
            }
        }

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

        // Сортируем по убыванию сходства
        scores.Sort((a, b) => b.Score.CompareTo(a.Score));

        var k = Math.Min(topK, scores.Count);
        var indices = new int[k];
        var distances = new float[k];

        for (var i = 0; i < k; i++)
        {
            indices[i] = scores[i].Index;
            // Преобразуем сходство в расстояние (1 - similarity)
            distances[i] = 1.0f - scores[i].Score;
        }

        return (indices, distances);
    }

    /// <summary>
    /// Вычисляет косинусное сходство между двумя векторами.
    /// </summary>
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
