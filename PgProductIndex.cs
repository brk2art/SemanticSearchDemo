using Microsoft.Extensions.AI;
using Npgsql;
using Pgvector;

// Same idea as ProductIndex, but the vectors live in PostgreSQL
// and the database does the similarity search.
public class PgProductIndex(NpgsqlDataSource db, IEmbeddingGenerator<string, Embedding<float>> generator) : IProductIndex
{
    public async Task InitializeAsync()
    {
        await using var cmd = db.CreateCommand("""
            CREATE TABLE IF NOT EXISTS products (
                id          int PRIMARY KEY,
                name        text NOT NULL,
                description text NOT NULL,
                embedding   vector(768) NOT NULL
            )
            """);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddRangeAsync(IReadOnlyList<Product> products)
    {
        var texts = products.Select(p => $"{p.Name}. {p.Description} {p.Uses}".Trim());
        var embeddings = await generator.GenerateAsync(texts);

        for (int i = 0; i < products.Count; i++)
        {
            await using var cmd = db.CreateCommand("""
                INSERT INTO products (id, name, description, embedding)
                VALUES ($1, $2, $3, $4)
                ON CONFLICT (id) DO UPDATE
                SET name = EXCLUDED.name,
                    description = EXCLUDED.description,
                    embedding = EXCLUDED.embedding
                """);
            cmd.Parameters.AddWithValue(products[i].Id);
            cmd.Parameters.AddWithValue(products[i].Name);
            cmd.Parameters.AddWithValue(products[i].Description);
            cmd.Parameters.AddWithValue(new Vector(embeddings[i].Vector));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<List<(Product Product, float Score)>> SearchAsync(string query, int top = 3)
    {
        var queryVector = new Vector(await generator.GenerateVectorAsync(query));

        await using var cmd = db.CreateCommand("""
            SELECT id, name, description, 1 - (embedding <=> $1) AS score
            FROM products
            ORDER BY embedding <=> $1
            LIMIT $2
            """);
        cmd.Parameters.AddWithValue(queryVector);
        cmd.Parameters.AddWithValue(top);

        var results = new List<(Product, float)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var product = new Product(reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
            results.Add((product, (float)reader.GetDouble(3)));
        }
        return results;
    }
}
