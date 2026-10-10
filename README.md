# SemanticSearchDemo

A minimal semantic search and question-answering app in C#. It shows the core ideas behind
vector search and RAG, and how to measure whether they work:

1. turn text into vectors and find the closest ones,
2. give the results to a chat model so it answers from that data only,
3. score both steps against a fixed set of questions.

Embeddings and chat run through `Microsoft.Extensions.AI`, using local models served by Ollama.
Vectors can be kept in memory or stored in PostgreSQL with pgvector.

## How it works

1. **Index:** each product's name and description is sent to an embedding model, which returns
   a vector (a list of numbers that represents the meaning of the text). The vector is stored
   next to the product.
2. **Search:** the question is embedded with the same model, compared with every stored vector
   using cosine similarity, and the closest products are returned.
3. **Answer:** the top 3 products are pasted into a prompt together with the question, and a
   chat model writes the answer from that text.

A question like "something to keep me dry when it rains" finds the rain jacket and the
waterproof boots, even though it shares almost no words with them.

## Cosine similarity, by hand and with a library

On startup the program compares two texts twice: once with a hand-written loop
(`VectorMath.Cosine`) and once with `TensorPrimitives.CosineSimilarity` from
`System.Numerics.Tensors`. Both print the same score, which shows what the library does.

## Two indexes, one interface

`IProductIndex` has two implementations:

- `ProductIndex` keeps vectors in memory and computes cosine similarity in C#.
- `PgProductIndex` stores vectors in PostgreSQL with pgvector and lets the database run the search
  with the `<=>` cosine distance operator.

Both return the same results and the same scores for the same question, because the model and
the maths are identical. Only the place where the search runs is different. The evaluation
below confirms this: it gives identical numbers on both.

## Answering questions (RAG)

`ShopAssistant` retrieves the top 3 products, builds a prompt from them and asks the chat model
to answer from that text only. If the text does not contain the answer, the model is told to
reply with one exact sentence: "I don't have that information."

Giving the model context is not enough to keep it honest. With the tent in the prompt,
"how much is the tent?" was answered with an invented price of $250, although no price exists
in the data. Without any context the same model had declined to answer. Two things reduced this:

- repeating the rule after the question, because small models follow the most recent
  instruction best,
- using a larger chat model (see the results below).

Run with `dotnet run -- debug` to print the exact prompt sent to the model.

## Evaluation

Run with `dotnet run -- eval`.

### Retrieval

`RetrievalEval.cs` holds 12 questions, each with the product ids that should be found, and
reports recall@k: of the expected products, the share that appears in the top k results.

| Embedding model | Dimensions | Recall@1 | Recall@3 |
| --- | --- | --- | --- |
| all-minilm | 384 | 0.75 | 0.92 |
| nomic-embed-text | 768 | 0.83 | 1.00 |

The smaller model matched words rather than needs: "I want hot food on the trail" ranked the
sleeping bag ("warm ... for cold nights") above the camping stove. The larger model fixed two
of the three real failures and broke nothing, at the cost of twice the storage per vector.

Scores from different models are not comparable, so any minimum-score threshold has to be
chosen again when the model changes.

Changing the embedding model changes the vector size, so the `products` table has to be
recreated (`vector(384)` to `vector(768)`) and every product re-embedded. In production this
would be a new column filled in the background, not a dropped table.

### Answers

`AnswerEval.cs` holds 16 questions: 6 the data can answer (each with words a correct answer
must contain) and 10 it cannot (price, stock, colour, delivery and similar), where the only
correct reply is the refusal sentence. The check is plain string comparison, with no model
involved.

| | llama3.2 (3B), first prompt | llama3.2 (3B), stricter prompt | llama3.1 (8B), stricter prompt |
| --- | --- | --- | --- |
| Answered when it should refuse | 3 of 10 | 3 of 10 | 0 of 10 |
| Refused when it should answer | 0 of 6 | 0 of 6 | 0 of 6 |
| Answered but missed the point | 0 of 6 | 0 of 6 | 1 of 6 |

Rewording the prompt changed the wording of the wrong answers and none of the count. Told
"never guess, estimate or assume", the small model answered that a headlamp is worn on the
head, "implying it requires power". The larger model removed all three failures.

One failure remains and is left in on purpose: asked about rain, the model names the rain
jacket and leaves out the waterproof boots, although both were retrieved.

### Judge model

Run with `dotnet run -- judge`.

`JudgeEval.cs` scores the 6 answerable questions from 1 to 5 with the Groundedness and
Completeness evaluators from `Microsoft.Extensions.AI.Evaluation.Quality`, using llama3.1:8b
as the judge.

| Question | Truly complete | Completeness | Groundedness |
| --- | --- | --- | --- |
| Anything for rain? | No, boots missing | 2 | 5 |
| Cook outdoors? | Yes | 2 | 5 |
| How many in the tent? | Yes | 2 | 4 |
| Keep drinks cold? | Yes | 2 | 5 |
| See in the dark? | Yes | 3 | 5 |
| Warm to sleep in? | Yes | 2 | 5 |

The judge could not tell a complete answer from an incomplete one: both scored 2. Its
explanations also contradicted each other. These evaluators are documented as tuned on GPT-4o,
and here a local 8B model was grading its own output. The string checks above remain the
trusted measure. The file is kept so the same test can be repeated with a stronger judge.

## Files

| File | Purpose |
| --- | --- |
| `Product.cs` | The data being searched |
| `VectorMath.cs` | Hand-written cosine similarity, kept for comparison |
| `IProductIndex.cs` | Interface shared by both indexes |
| `ProductIndex.cs` | In-memory index |
| `PgProductIndex.cs` | PostgreSQL index using pgvector |
| `ShopAssistant.cs` | Retrieve, build the prompt, generate the answer |
| `RetrievalEval.cs` | Golden set and recall@k for search |
| `AnswerEval.cs` | Golden set and refusal checks for answers |
| `JudgeEval.cs` | Groundedness and completeness scored by a judge model |
| `Program.cs` | Sets up the models and the index, then runs the chat loop or an evaluation |

## Packages

- `Microsoft.Extensions.AI`: the `IEmbeddingGenerator` and `IChatClient` abstractions, so the provider can be swapped
- `OllamaSharp`: the Ollama client that implements both
- `System.Numerics.Tensors`: hardware-accelerated cosine similarity
- `Npgsql`: the PostgreSQL driver
- `Pgvector`: adds the `vector` type to Npgsql
- `Microsoft.Extensions.AI.Evaluation` and `Microsoft.Extensions.AI.Evaluation.Quality`: the judge-model evaluators

## Run it

Requires the .NET SDK, [Ollama](https://ollama.com) and, for the PostgreSQL index, Docker.

Pull the models once:

```bash
ollama pull nomic-embed-text
ollama pull llama3.1:8b
```

Chat with the in-memory index:

```bash
dotnet run
```

Chat with the PostgreSQL index:

```bash
docker run -d --name pgvector -e POSTGRES_PASSWORD=postgres -p 5432:5432 pgvector/pgvector:pg17
docker exec -it pgvector psql -U postgres -c "CREATE EXTENSION IF NOT EXISTS vector;"
dotnet run -- pg
```

Other modes, which can be combined with `pg`:

| Command | What it does |
| --- | --- |
| `dotnet run -- debug` | Chat, and print the exact prompt sent to the model |
| `dotnet run -- eval` | Run the retrieval and answer evaluations, then exit |
| `dotnet run -- judge` | Run the judge-model evaluation, then exit |

The answer and judge evaluations make one or more model calls per question, so they take
several minutes on a machine without a GPU.

## Limits (on purpose)

This is a learning project, not production code:

- The golden sets are small (12 and 16 questions), so one question moves a score by several points.
- With 7 products the top 3 covers almost half the catalogue, so recall@3 is easy to reach.
- Answer quality is checked by required words, which cannot tell a good answer from a poor one
  that contains the right word. The judge model meant to cover this was not reliable.
- Every start re-embeds all products, even when nothing has changed.
- There is no vector index, so the search compares the question with every row.
- There is no keyword search, so exact matches such as product codes work poorly.
- Every question returns three products, even an unrelated one, because there is no minimum score.
- Attributes that are not in the text, such as price and stock, cannot be answered at all.
- Each question stands alone; there is no conversation history.

## Next steps

- Grow the catalogue and add harder questions, so the evaluation can measure further gains.
- Add hybrid search (keyword plus vector) and reranking, each measured against the golden set.
- Choose a minimum score from the evaluation data.
- Repeat the judge evaluation with a stronger model.
- Answer price and stock questions through tool calling instead of the prompt.