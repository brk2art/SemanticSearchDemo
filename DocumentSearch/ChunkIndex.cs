using Microsoft.Extensions.AI;
using System.Numerics.Tensors;

public class ChunkIndex(IEmbeddingGenerator<string, Embedding<float>> generator)
{
    private readonly List<(DocChunk Chunk, ReadOnlyMemory<float> Vector)> _items = [];

    public async Task AddRangeAsync(IReadOnlyList<DocChunk> chunks)
    {
        var embeddings = await generator.GenerateAsync(chunks.Select(c => c.Text));
        for (int i = 0; i < chunks.Count; i++)
            _items.Add((chunks[i], embeddings[i].Vector));
    }

    public async Task<List<(DocChunk Chunk, float Score)>> SearchAsync(string query, int top)
    {
        var queryVector = await generator.GenerateVectorAsync(query);

        return _items
            .Select(item => (item.Chunk, Score: TensorPrimitives.CosineSimilarity(queryVector.Span, item.Vector.Span)))
            .OrderByDescending(r => r.Score)
            .Take(top)
            .ToList();
    }
}