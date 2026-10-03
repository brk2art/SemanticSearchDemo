# SemanticSearchDemo

A minimal semantic search in C#. It shows the core idea behind vector search and RAG:
turn text into vectors, then find the closest ones.

Embeddings are generated through `Microsoft.Extensions.AI`, using a local model served by Ollama.
Vectors can be kept in memory or stored in PostgreSQL with pgvector.

## How it works

1. **Index:** each product's name and description is sent to an embedding model, which returns
   a vector (a list of numbers that represents the meaning of the text). The vector is stored
   next to the product.
2. **Search:** the query is embedded with the same model, compared with every stored vector
   using cosine similarity, and the closest products are returned.

A query like "something to keep me dry when it rains" finds the rain jacket and the waterproof
boots, even though it shares almost no words with them.

## Cosine similarity, by hand and with a library

On startup the program compares two texts twice: once with a hand-written loop
(`VectorMath.Cosine`) and once with `TensorPrimitives.CosineSimilarity` from
`System.Numerics.Tensors`. Both print the same score, which shows what the library does.

## Two indexes, one interface

`IProductIndex` has two implementations:

- `ProductIndex` keeps vectors in memory and computes cosine similarity in C#.
- `PgProductIndex` stores vectors in PostgreSQL with pgvector and lets the database run the search
  with the `<=>` cosine distance operator.

Both return the same results for the same query, because the model and the maths are identical.
Only the place where the search runs is different.

## Files

| File | Purpose |
| --- | --- |
| `Product.cs` | The data being searched |
| `VectorMath.cs` | Hand-written cosine similarity, kept for comparison |
| `IProductIndex.cs` | Interface shared by both indexes |
| `ProductIndex.cs` | In-memory index |
| `PgProductIndex.cs` | PostgreSQL index using pgvector |
| `Program.cs` | Creates the embedding generator, picks an index and runs an interactive search |

## Packages

- `Microsoft.Extensions.AI`: the `IEmbeddingGenerator` abstraction, so the provider can be swapped
- `OllamaSharp`: the Ollama client that implements it
- `System.Numerics.Tensors`: hardware-accelerated cosine similarity
- `Npgsql`: the PostgreSQL driver
- `Pgvector`: adds the `vector` type to Npgsql

## Run it

Requires the .NET SDK, [Ollama](https://ollama.com) and, for the PostgreSQL index, Docker.

In-memory index:

```bash
ollama pull all-minilm
dotnet run
```

PostgreSQL index:

```bash
docker run -d --name pgvector -e POSTGRES_PASSWORD=postgres -p 5432:5432 pgvector/pgvector:pg17
docker exec -it pgvector psql -U postgres -c "CREATE EXTENSION IF NOT EXISTS vector;"
dotnet run -- pg
```

## Limits (on purpose)

This is a learning project, not production code:

- Every start re-embeds all products, even when nothing has changed.
- There is no vector index, so the search compares the query with every row.
- There is no keyword search, so exact matches such as product codes work poorly.
- Negation is handled poorly: "not waterproof" still returns waterproof products.
- Attributes that are not in the text, such as price, cannot be searched.
- Every query returns results, even a meaningless one, because there is no minimum score.
- There is no evaluation of result quality.

## Next steps

- Add metadata filters (price, category) and a minimum score.
- Skip the model call when a product's text has not changed.
- Add an HNSW index.
- Add hybrid search (keyword plus vector) and a small test set to measure quality.