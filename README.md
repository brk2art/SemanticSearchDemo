# SemanticSearchDemo

A minimal semantic search in C#. It shows the core idea behind vector search and RAG in
about 100 lines: turn text into vectors, then find the closest ones.

Embeddings are generated through `Microsoft.Extensions.AI`, using a local model served by Ollama.

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
The search itself uses the library version.

## Files

| File | Purpose |
| --- | --- |
| `Product.cs` | The data being searched |
| `VectorMath.cs` | Hand-written cosine similarity, kept for comparison |
| `ProductIndex.cs` | In-memory vector store with add and search |
| `Program.cs` | Creates the embedding generator, indexes sample products and runs an interactive search |

## Packages

- `Microsoft.Extensions.AI`: the `IEmbeddingGenerator` abstraction, so the provider can be swapped
- `OllamaSharp`: the Ollama client that implements it
- `System.Numerics.Tensors`: hardware-accelerated cosine similarity

## Run it

Requires the .NET SDK and [Ollama](https://ollama.com).

```bash
ollama pull all-minilm
dotnet run
```

## Limits (on purpose)

This is a learning project, not production code:

- Vectors live in memory and are rebuilt on every start.
- Search compares the query with every product, which does not scale.
- There is no keyword search, so exact matches such as product codes work poorly.
- Negation is handled poorly: "not waterproof" still returns waterproof products.
- Attributes that are not in the text, such as price, cannot be searched.
- Every query returns results, even a meaningless one, because there is no minimum score.
- There is no evaluation of result quality.

## Next steps

- Store vectors in PostgreSQL with pgvector.
- Add metadata filters (price, category) and a minimum score.
- Add hybrid search (keyword plus vector) and a small test set to measure quality.