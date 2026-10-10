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

## Answering questions (RAG)

After searching, the program passes the top 3 products to a chat model (`llama3.2`)
together with the question, and the model writes the answer from that text only.

Giving the model context is not enough to keep it honest. With the tent in the prompt,
"how much is the tent?" was answered with an invented price of $250, although no price
exists in the data. Repeating the rule after the question ("answer only from the product
text above, otherwise reply: I don't have that information") stopped this in the cases
tested.

Run with `dotnet run -- debug` to print the exact prompt sent to the model.

## Evaluation

`RetrievalEval.cs` holds a golden set of 12 questions, each with the product ids that
should be found, and reports recall@k: of the expected products, the share that appears
in the top k results. Run it with `dotnet run -- eval`.

| Embedding model | Dimensions | Recall@1 | Recall@3 |
| --- | --- | --- | --- |
| all-minilm | 384 | 0.75 | 0.92 |
| nomic-embed-text | 768 | 0.83 | 1.00 |

The larger model fixed two of the three real failures and broke nothing, at the cost of
twice the storage per vector. Scores from different models are not comparable, so any
minimum-score threshold has to be chosen again when the model changes.

Changing the embedding model changes the vector size, so the `products` table has to be
recreated (`vector(384)` to `vector(768)`) and every product re-embedded. In production
this would be a new column filled in the background, not a dropped table.

## Files

| File | Purpose |
| --- | --- |
| `Product.cs` | The data being searched |
| `VectorMath.cs` | Hand-written cosine similarity, kept for comparison |
| `IProductIndex.cs` | Interface shared by both indexes |
| `ProductIndex.cs` | In-memory index |
| `PgProductIndex.cs` | PostgreSQL index using pgvector |
| `Program.cs` | Creates the embedding generator, picks an index and runs an interactive search |
| `RetrievalEval.cs` | Golden set and recall@k |


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
ollama pull nomic-embed-text
ollama pull llama3.2
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
- Only retrieval is evaluated; answer quality is not measured yet.
- The golden set is small (12 questions), so one question moves the score by 8 points.

## Next steps

- Add metadata filters (price, category) and a minimum score.
- Skip the model call when a product's text has not changed.
- Add an HNSW index.
- Add hybrid search (keyword plus vector) and a small test set to measure quality.