# SemanticSearchDemo

A minimal semantic search and question-answering app in C#. It shows the core ideas behind
vector search and RAG, and how to measure whether they work:

1. turn text into vectors and find the closest ones,
2. improve the ranking with enrichment and a reranker,
3. give the results to a chat model so it answers from that data only,
4. score each step against fixed sets of questions.

Embeddings and chat run through `Microsoft.Extensions.AI`, using local models served by Ollama.
Vectors can be kept in memory or stored in PostgreSQL with pgvector.

## How it works

**Indexing (once, when data changes)**

1. **Enrich:** a chat model writes, for each product, five needs a customer might express that
   the product meets. The result is cached in `uses.json`.
2. **Embed:** the product's name, description and generated needs are sent to an embedding
   model, which returns a vector (a list of numbers that represents the meaning of the text).
3. **Store:** the vector is stored next to the product.

**Answering (on every question)**

1. **Retrieve:** the question is embedded with the same model and compared with every stored
   vector using cosine similarity. The 10 closest products are kept.
2. **Rerank:** a chat model reads the question next to those 10 products and reorders them.
   The best 3 go forward.
3. **Answer:** the 3 products are pasted into a prompt together with the question, and a chat
   model writes the answer from that text.

A question like "something to keep me dry when it rains" finds the rain jacket, the waterproof
boots and the poncho, even though it shares almost no words with them.

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
the maths are identical. Only the place where the search runs is different.

`RerankingIndex` is a third implementation that wraps either of the other two, so reranking
can be switched on without changing the code that uses the index.

## Enrichment

Product descriptions state features; customers ask about needs. "Bright LED light worn on the
head" never says the product leaves your hands free, so a question about free hands could not
find it, at any depth.

`Enricher` asks a chat model for the needs each product meets and adds them to the text that
gets embedded. The generated text is used for search only. It is never shown to the customer
or to the answering model.

The first version of the prompt produced poor text: every product began with the same
introductory sentence, many lines were generic ("I'm going on a camping trip" under seven
products), and some were wrong (the two-person tent for "a family camping trip with kids",
the cold-drinks bottle for "my morning coffee"). The second version asks for needs the
description states or directly implies, focused on what sets the product apart, and strips
headings and bullets in code. Generated index text needs the same review as generated answers.

## Reranking

An embedding turns each text into a vector separately, without seeing the other. To it, a
bottle that keeps drinks cold and a flask that keeps them hot are neighbours. A reranker reads
the question and each candidate together, so it can tell them apart.

`RerankingIndex` fetches 10 candidates from the inner index, asks the chat model to order them
by how well they meet the customer's need, and returns the best ones. If the model's reply
cannot be read, the original order is kept.

A reranker only reorders what it is given. It cannot promote a product that is not among the
candidates, which is the gap enrichment fills.

Using a general chat model as the reranker is slow (one model call per question). Production
systems use small dedicated reranking models for the same design.

## Answering questions (RAG)

`ShopAssistant` builds a prompt from the top 3 products and asks the chat model to answer from
that text only. If the text does not contain the answer, the model is told to reply with one
exact sentence: "I don't have that information."

Giving the model context is not enough to keep it honest. With the tent in the prompt,
"how much is the tent?" was answered with an invented price of $250, although no price exists
in the data. Without any context the same model had declined to answer. Two things reduced this:

- repeating the rule after the question, because small models follow the most recent
  instruction best,
- using a larger chat model (see the results below).

Run with `dotnet run -- debug` to print the exact prompt sent to the model.

## Evaluation

### Retrieval

Run with `dotnet run -- eval`.

`RetrievalEval.cs` reports recall@k: of the products that should be found for a question, the
share that appears in the top k results. It holds two sets of questions:

- **Tuned set (26 questions):** used while choosing the model, the text and the prompts.
  Every failure in it was looked at and worked on, so its score is optimistic.
- **Held-out set (19 questions):** never used for tuning, and phrased as needs
  ("my back hurts from sleeping on the ground"). This is the fair measure.

Results on the 30-product catalogue, with nomic-embed-text and llama3.1:8b:

| Held-out set (19) | Recall@1 | Recall@3 |
| --- | --- | --- |
| Vector search only | 0.42 (8 right) | 0.79 (15 right) |
| + enrichment | 0.58 (11 right) | 0.84 (16 right) |
| + reranking | 0.74 (14 right) | 0.95 (18 right) |
| + both | 0.79 (15 right) | 0.95 (18 right) |

| Tuned set (26) | Recall@1 | Recall@3 |
| --- | --- | --- |
| Vector search only | 0.79 | 0.96 |
| + enrichment | 0.79 | 1.00 |
| + reranking | 0.87 | 0.96 |
| + both | 0.91 | 1.00 |

What these show:

- **The tuned set hid the problem.** It reported 0.79 and 0.96 for plain vector search; on
  unseen questions the same system scored 0.42 and 0.79.
- **Reranking is the main gain.** Alone it puts the right product first for six more of the
  19 held-out questions.
- **Enrichment adds little on top, but does a different job.** One more held-out question at
  rank 1. Its real contribution is getting a product into the candidate list: one tuned
  question failed with either technique alone and passed with both.
- **Recall@10 was 1.00 on the held-out set throughout.** The right product was always found;
  it was ranked badly.
- **An unchanged average can hide two changes.** Enrichment left tuned recall@1 at 0.79 while
  fixing one question and breaking another.
- **0.91 is the highest tuned recall@1 possible**, because four questions have several correct
  products and one slot.

Both techniques are on by default. Four held-out questions still fail at rank 1 and are left
as they are: adjusting the system to pass them would turn the held-out set into a second
tuned set.

Earlier, on a 7-product catalogue with 12 questions, the embedding model was chosen the
same way:

| Embedding model | Dimensions | Recall@1 | Recall@3 |
| --- | --- | --- | --- |
| all-minilm | 384 | 0.75 | 0.92 |
| nomic-embed-text | 768 | 0.83 | 1.00 |

Scores from different models are not comparable, so any minimum-score threshold has to be
chosen again when the model changes. Changing the embedding model also changes the vector
size, so the `products` table has to be recreated and every product re-embedded. In production
this would be a new column filled in the background, not a dropped table.

### Answers

Run with `dotnet run -- answers`.

`AnswerEval.cs` holds 16 questions: 6 the data can answer (each with words a correct answer
must contain) and 10 it cannot (price, stock, colour, delivery and similar), where the only
correct reply is the refusal sentence. The check is plain string comparison.

These results were measured on the earlier 7-product catalogue and have not yet been repeated
on the larger one:

| | llama3.2 (3B), first prompt | llama3.2 (3B), stricter prompt | llama3.1 (8B), stricter prompt |
| --- | --- | --- | --- |
| Answered when it should refuse | 3 of 10 | 3 of 10 | 0 of 10 |
| Refused when it should answer | 0 of 6 | 0 of 6 | 0 of 6 |
| Answered but missed the point | 0 of 6 | 0 of 6 | 1 of 6 |

Rewording the prompt changed the wording of the wrong answers and none of the count. Told
"never guess, estimate or assume", the small model answered that a headlamp is worn on the
head, "implying it requires power". The larger model removed all three failures.

### Judge model

Run with `dotnet run -- judge`.

`JudgeEval.cs` scores the 6 answerable questions from 1 to 5 with the Groundedness and
Completeness evaluators from `Microsoft.Extensions.AI.Evaluation.Quality`, using llama3.1:8b
as the judge. Also measured on the 7-product catalogue:

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
and here a local 8B model was grading its own output. The string checks remain the trusted
measure. The file is kept so the same test can be repeated with a stronger judge.

## Files

| File | Purpose |
| --- | --- |
| `Product.cs` | The data being searched |
| `Catalog.cs` | The 30 products |
| `VectorMath.cs` | Hand-written cosine similarity, kept for comparison |
| `IProductIndex.cs` | Interface shared by all indexes |
| `ProductIndex.cs` | In-memory index |
| `PgProductIndex.cs` | PostgreSQL index using pgvector |
| `RerankingIndex.cs` | Wraps an index and reorders its top 10 with a chat model |
| `Enricher.cs` | Generates the needs each product meets, for search |
| `uses.json` | Cached output of the enricher |
| `ShopAssistant.cs` | Retrieve, build the prompt, generate the answer |
| `RetrievalEval.cs` | Tuned and held-out sets, recall@k |
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

Pull the models once, and make sure Ollama is running:

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

Arguments, which can be combined:

| Argument | What it does |
| --- | --- |
| `eval` | Run the retrieval evaluation, then exit |
| `answers` | Run the answer evaluation, then exit |
| `judge` | Run the judge-model evaluation, then exit |
| `no-enrich` | Embed name and description only |
| `no-rerank` | Skip the reranker and use vector order |
| `debug` | In chat, print the exact prompt sent to the model |
| `pg` | Use PostgreSQL instead of the in-memory index |

`dotnet run -- eval no-enrich no-rerank` reproduces the plain vector-search baseline in about
a second. With reranking on, every question costs a model call, so an evaluation run takes
10 to 15 minutes on a machine without a GPU.

To regenerate the enrichment text after changing its prompt, delete `uses.json`.

## Limits (on purpose)

This is a learning project, not production code:

- The question sets are small (26 and 19), so one question moves a score by several points.
- Some expected answers are debatable ("the stove won't light" expects the gas canister; the
  stove is a fair reply), and only one person labelled them.
- Reranking uses a general chat model, which is far too slow for a live system.
- Each question costs two chat-model calls, one to rerank and one to answer.
- Answer quality is checked by required words, which cannot tell a good answer from a poor one
  that contains the right word. The judge model meant to cover this was not reliable.
- The answer and judge evaluations still use questions written for the 7-product catalogue.
- The enrichment cache is keyed by product id only, so a prompt change needs a manual delete.
- Every start re-embeds all products, even when nothing has changed.
- There is no vector index, so the search compares the question with every row.
- There is no keyword search. Product codes are found by vector search at this size, which
  would not hold with thousands of similar codes.
- Every question returns three products, even an unrelated one, because there is no minimum score.
- Attributes that are not in the text, such as price and stock, cannot be answered at all.
- Each question stands alone; there is no conversation history.

## Next steps

- Test hybrid search (keyword plus vector) against the held-out set.
- Update the answer evaluation for the larger catalogue and re-run it end to end.
- Repeat the judge evaluation with a stronger model.
- Move from one-line products to real documents, which need chunking.
- Answer price and stock questions through tool calling instead of the prompt.