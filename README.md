# SemanticSearchDemo

A minimal semantic search and question-answering app in C#. It shows the core ideas behind
vector search and RAG, and how to measure whether they work:

1. turn text into vectors and find the closest ones,
2. improve the ranking with enrichment and a reranker,
3. give the results to a chat model so it answers from that data only,
4. let the model look up live data, such as price and stock, through tools,
5. score each step against fixed sets of questions.

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

## Indexes behind one interface

`IProductIndex` has two base implementations:

- `ProductIndex` keeps vectors in memory and computes cosine similarity in C#.
- `PgProductIndex` stores vectors in PostgreSQL with pgvector and lets the database run the search
  with the `<=>` cosine distance operator.

Both return the same results and the same scores for the same question, because the model and
the maths are identical. Only the place where the search runs is different.

`RerankingIndex` and `HybridIndex` implement the same interface and wrap another index, so
each technique can be switched on without changing the code that uses the index.

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

## Hybrid search (measured, not adopted)

`HybridIndex` runs keyword search next to vector search and merges the two rankings. The
keyword side is BM25, written by hand: a word counts for more if it is rare in the catalogue.
The merge is reciprocal rank fusion: each product gets `1 / (60 + rank)` from each list, and
the sums decide the order. Ranks are used because similarity scores and keyword scores are on
unrelated scales.

Here it made results worse, so it is off by default:

| Enrichment on, no reranker | Vector only | Hybrid |
| --- | --- | --- |
| Held-out Recall@1 | 0.58 | 0.53 |
| Held-out Recall@3 | 0.84 | 0.68 |

Each regression was one incidental word matching the wrong product. "Something to carry my
lunch and a jacket" returned two jackets. "I need to open a can" returned the hiking sandals,
described as "open sandals". "I got a small cut" returned the daypack, a "small" backpack.

This says something about this setup, not about hybrid search in general. The questions are
needs, not product names. With 30 products, a word that appears once looks rare and important,
which a large catalogue would correct. And the implementation has no stemming and weights both
lists equally. In a catalogue with thousands of similar part codes the same test would likely
come out the other way. Product codes are already found by vector search at this size.

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

## Documents (chunking and citations)

Shop knowledge also lives in documents. The `docs` folder holds four: returns, delivery,
warranty and a stove manual. Run with `dotnet run -- docs`.

Documents are split into chunks, one per section, and each chunk starts with its heading
path, for example `# Camping stove CS-400: user manual ## Before first use`. The top 3 chunks
are numbered in the prompt, and the answer cites them: "Express delivery costs €9.95 [1]".
Code reads the numbers back and checks that the cited section is the one that holds the answer.

Fifteen questions (twelve answerable, three that no document answers), measured with four
chunking methods:

| | Whole documents | Fixed 40 words | Sections | Sections + headers |
| --- | --- | --- | --- | --- |
| Correct answers | 15 of 15 | 15 of 15 | 15 of 15 | 15 of 15 |
| Words sent per question | 182 | 125 | 100 | 117 |
| Weakest real match | 0.594 | 0.674 | 0.677 | 0.678 |
| Strongest near miss | 0.625 | 0.656 | 0.671 | 0.619 |

Every method answered correctly, so the deciding measure was separation. With whole
documents, "how do I clean my tent?" (which no document answers) scored higher than
"when will I get my refund?". With headers, every real match scored above every near miss.

Two findings:

- **Cutting at headings lost context.** "CS-400" appeared only in the manual's title, so the
  section chunks no longer contained it, and the model answered that "the CS-400 stove is
  not mentioned". Writing the heading path into every chunk fixed it.
- **Adding citations broke refusals.** Five correct, cited answers ended with "I don't have
  that information." Splitting the instruction into two explicit cases ("if the documents
  contain the answer... / if they do not, reply with exactly this sentence and nothing else")
  fixed all five without losing a citation.

The default chunker is `HeaderChunker` from `Microsoft.Extensions.DataIngestion` (preview).
It was measured against the hand-written section chunker and scored the same (15 of 15,
12 of 12 citations, 119 against 118 words). It is used for what it adds: size limits that
split long sections while keeping their headings, readers for PDF and Word, and table
handling. The hand-written chunkers stay as references.

## Tool calling (price and stock)

Price and stock change too often to embed in the product text. They come from tools instead:
C# methods the model can ask for. The model never runs anything. It replies with a request
("call GetPriceAndStock with TN-400"), `UseFunctionInvocation()` runs the method and sends the
result back, and the model writes the answer. A tool question therefore costs at least two
answering calls.

`ShopTools` has two tools:

- `GetPriceAndStock(code)` returns the price and stock for one product.
- `FindProducts(word)` returns every product whose name contains a word, most expensive first.

The second exists because search cannot answer "all" or "most". Asked "which tent is the most
expensive?", the top 3 search results held two tents and a sleeping mat. The ultralight tent
came fourth, so the model compared two tents and was right by luck. `FindProducts` reads the
whole catalogue, so the list is complete by construction.

Three things around the model make it reliable:

- **Routing.** `ToolRouter` first asks the chat model one yes-or-no question: does this need
  live price or stock? On "no", the answering model gets no tools and no tool rule. A prompt
  rule saying "do not call any tool" was ignored; removing the tools worked.
- **Only the final message.** `response.Text` joins the text of every message in the tool loop.
  In one run the model wrote invented prices next to its real tool calls, then answered
  correctly once the real results came back, and the customer saw both. The answer is now the
  last assistant message only.
- **A guard.** Before an answer is returned, code checks it for tool-call text and for any price
  that no tool returned (sums, differences and 2 to 10 times a returned price are allowed). On a
  problem the answer is written again from the tool results with no tools offered. If it still
  fails, a fallback asks the customer to contact staff.

Run with `dotnet run -- tools`. Add `trace` to print every message sent to the model, on every
call. `dotnet run -- guardtest` checks the guard on fixed examples.

## Structured output (the model reads, code writes)

With tool calling, the model writes the sentence that states the price or stock, so it can
state it wrong. In one run it answered "we have the ultralight tent in stock" after the tool
returned "out of stock". The guard could not see it: there was no wrong number.

With `structured`, the model only describes the question, as JSON that follows a schema made
from the `ShopRequest` record:

    {"kind":"compare","codes":["LN-060","FL-055"],"category":"","quantity":1}

`AnswerWriter` then fetches the data and writes the sentence, including any total, in code.
Codes the model returns are checked against the search results, like any other input.

Two findings:

- **The schema controls the shape, only the prompt controls the meaning.** The schema is sent
  to Ollama as the output format, which guarantees valid JSON, but the model never reads it.
  With field descriptions only in the schema, 0 of 15 kinds were right ("kind": "TN-400").
  With the schema also pasted into the prompt, 11 of 15.
- **Don't ask the model to make distinctions code can make.** "Price" against "total" and
  "cheapest" for two named products confused it. Merging totals into "price" (code adds them)
  and renaming the comparison "compare" took it to 14 of 15. The 15th followed the new
  definition; the test label had not been updated.

Run with `dotnet run -- tooleval structured`, or `dotnet run -- extract` to check only the
extraction.

## MCP server (sharing the tools)

`ShopMcpServer` publishes `GetPriceAndStock` and `FindProducts` through the Model Context
Protocol, so any MCP client can use them: this app, GitHub Copilot, or an agent. It links the
same `ShopTools.cs` as the main project, so there is one copy of the business code and two hosts.
`ShopMcpTools` is a thin adapter over it, like a controller over a service.

The server uses the stdio transport: the client starts it as a child process and they exchange
JSON-RPC messages over stdin and stdout. Anything else written to stdout breaks the protocol,
so the server sends its console output to stderr.

With `mcp`, the app asks the server for its tools (`ListToolsAsync`) instead of creating them
itself. The model sees the same names, descriptions and schemas, and the 22 tool questions
scored the same: 12 of 12 tuned, 10 of 10 held out. Two details made that comparison fair: the
SDK publishes method names in snake_case, so the names are set explicitly to match the prompt,
and calls are recorded in the client's `FunctionInvoker` hook, because the tools now run in
another process.

Copilot in Visual Studio used the same server with no code changes, through a `.mcp.json` file.
Asked for the family tent's price, it called `FindProducts`: it had no product list, so it could
not have known the code. Tools meant for clients you don't control must work without your app's
context.

Build with `dotnet build SemanticSearchDemo.slnx`, which also rebuilds the server, then run
`dotnet run -- tools mcp`.

## Agent or workflow: who decides the next step?

A customer describes a trip; the shop answers with a shopping list and a total. Two designs do the same job:

- **Agent** (`ChatClientAgent`, Microsoft Agent Framework): the model gets `SearchProducts` and `SubmitShoppingList` and decides what to call, how often, and when to stop. The tools check what code can check: codes must come from a search, stock must cover the quantity, totals are computed in code, repeats and calls past 12 are refused.
- **Workflow** (`ShopWorkflow`): code runs five fixed steps (plan → search → pick → check → answer). The model makes three small structured decisions: what is needed, which product fits each need, and one sentence of advice. Code searches, checks stock, computes totals and writes every sentence that states a fact.

`AgentEval` scores both on five goals: a couple camping who already own a tent, a rainy day hike, a family of four with no gear, the out-of-stock ultralight tent, and kayaks (not sold).

```
dotnet run -- workfloweval qwen
dotnet run -- agenteval qwen
```

| Design (qwen2.5:7b, local) | Goals met | Time, 5 goals |
|---|---|---|
| Agent | 2 / 5 | ~1,040 s |
| Agent with stricter tool guards | 0 / 5 | – |
| Workflow | 3 / 5 | 333 s |

**Why the agent failed:** it stopped after describing a plan, looped (34 identical searches on one goal), and ignored tool refusals. Stricter tools made it worse, because a tool can refuse but only the model can end the loop.

**Where the workflow still fails:** only where the model judges meaning.
- No sleeping mats for the couple: the plan didn't list them.
- Kayak: the picker chose hiking sandals for "kayak with paddle"; code then reported, correctly and uselessly, that the sandals are out of stock.
- The family goal passed with two tents (one picked for "camp chairs"), because the eval doesn't check for extra items.

Code checked stock, quantities and totals without a mistake, and no invented price appeared once code wrote the fact sentences.

**Spec decisions (made before the runs):** for the ultralight tent, suggesting another tent is fine if the answer says plainly it's out of stock. For kayaks: no list, and no offer to order.

**Why 3 / 5 stands:** every change was made by reading these same five goals; another round would tune against the test set. Next fixes, not built: a relevance gate in code (minimum rerank score) so "nothing fits" is a code decision, trip knowledge in code, and an extra-items check.

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

Enrichment and reranking are on by default. Four held-out questions still fail at rank 1 and
are left as they are: adjusting the system to pass them would turn the held-out set into a
second tuned set.

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

`AnswerEval.cs` holds 20 questions: 10 the data can answer (each with words a correct answer
must contain) and 10 it cannot (price, stock, colour, delivery and similar), where the only
correct reply is the refusal sentence. The check is plain string comparison.

With the full chain on the 30-product catalogue:

| | Result |
| --- | --- |
| Answered when it should refuse | 0 of 10 |
| Refused when it should answer | 0 of 10 |
| Answered but missed the point | 1 of 10 |

The one failure: asked about rain, the model named the poncho and the jacket and left out the
waterproof boots, although all three were retrieved. The boots' description says "wet weather"
and never "rain". The same omission appeared on the 7-product catalogue with a different
retrieval setup, which shows that improving retrieval cannot fix a generation failure.

This set does not prove that better retrieval improved answers: its answerable questions are
easy to retrieve for.

The chat model was chosen on the earlier 7-product catalogue, with 16 questions:

| | llama3.2 (3B), first prompt | llama3.2 (3B), stricter prompt | llama3.1 (8B), stricter prompt |
| --- | --- | --- | --- |
| Answered when it should refuse | 3 of 10 | 3 of 10 | 0 of 10 |
| Refused when it should answer | 0 of 6 | 0 of 6 | 0 of 6 |

Rewording the prompt changed the wording of the wrong answers and none of the count. Told
"never guess, estimate or assume", the small model answered that a headlamp is worn on the
head, "implying it requires power". The larger model removed all three failures.

### Judge model

Run with `dotnet run -- judge`.

`JudgeEval.cs` scores 6 answerable questions from 1 to 5 with the Groundedness and
Completeness evaluators from `Microsoft.Extensions.AI.Evaluation.Quality`, using llama3.1:8b
as the judge. Measured on the 7-product catalogue and not repeated since:

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

### Tools

Run with `dotnet run -- tooleval` (about 20 minutes).

`ToolEval.cs` holds 12 tuned questions and 10 held-out questions written after the router, in
different words. Each lists the tool calls it should make (`TN-400`, `find:tent`, or none) and
words a correct answer must contain. A row is marked MISSED (a needed call not made), EXTRA (a
call not needed), BROKEN (tool-call text in the answer), INVENTED (a price no tool returned) or
ANSWER (calls right, answer incomplete).

One change at a time:

| Change | Tuned (12) | Held out (10) | Tool calls, tuned (minimum 12) |
| --- | --- | --- | --- |
| One tool, no rule | 5 | | 13 |
| Rule: when to call, once per product | 6 | | 18 |
| Routing | 9 | 8 | 12 |
| Rule: state each price, give totals | 10 | 8 | 12 |
| Second tool, `FindProducts` | 10 | 9 | 16 |
| Answer is the final message only | 12 | 9 | 16 |
| Quantities need one call; guard | 12 | 10 | 12 |

What these show:

- **The first two runs measured nothing.** The tool rule was built and never sent. `debug`
  showed it anyway, because it prints what the code meant to send. The trace, which sits in the
  chat client pipeline, shows what was actually sent.
- **Wording made the model call more, never less.** "Call it once for each product" fixed every
  missed comparison. "Do not call any tool" changed nothing, and the return-policy question went
  from one unneeded lookup to three. Routing brought unneeded calls to zero.
- **The `FindProducts` row first scored 12 of 12.** One answer held invented prices next to the
  correct one and passed the word check. The BROKEN and INVENTED checks were added, and the
  honest score was 10.
- **Two expected answers were widened after review** ("€39.05 cheaper"; "we don't have the
  ultralight tent in stock"). Both were judged correct on their merits.
- **Identical runs can differ.** One row said "in stock" in one run and "out of stock" in the
  next, so a one-row change can be noise.

After the final change, the answer and retrieval evaluations were rerun and gave the same results.

The same 22 questions with `structured`: 12 of 12 tuned and 9 of 10 held out. The kayak is
refused without checking the catalogue, because the model returns "other" with no product to
look up. It took 139 to 168 seconds against 217 for tool calling, with one model call fewer per
price question. Every price and stock statement is written by code, so none can contradict the
data. The answers read like a form ("We have 6 of the water filter in stock") and don't
answer the question as asked ("is it under 10 euros?" gets only the price).

## Files

| File | Purpose |
| --- | --- |
| `Program.cs` | Sets up the models and indexes, then runs the chat loop or an evaluation |
| `ProductSearch/Product.cs` | The data being searched |
| `ProductSearch/Catalog.cs` | The 30 products |
| `ProductSearch/Enricher.cs` | Generates the needs each product meets, for search |
| `ProductSearch/IProductIndex.cs` | Interface shared by all product indexes |
| `ProductSearch/ProductIndex.cs` | In-memory index |
| `ProductSearch/PgProductIndex.cs` | PostgreSQL index using pgvector |
| `ProductSearch/RerankingIndex.cs` | Wraps an index and reorders its top 10 with a chat model |
| `ProductSearch/HybridIndex.cs` | Wraps an index and fuses it with BM25 keyword search |
| `ProductSearch/ShopAssistant.cs` | Retrieve, offer tools when routed, generate, check the answer |
| `ProductSearch/AnswerWriter.cs` | Writes price, stock and comparison answers from data |
| `Tools/ShopTools.cs` | `GetPriceAndStock` and `FindProducts`, recording every call and result |
| `Tools/ToolRouter.cs` | Decides whether a question needs live price or stock |
| `Tools/ShopRequest.cs` | The request record (the schema) and `RequestExtractor` |
| `Evaluation/ToolEval.cs` | Tuned and held-out tool questions, call and answer checks |
| `DocumentSearch/DocChunk.cs` | A chunk: source file, section and text |
| `DocumentSearch/Chunker.cs` | Hand-written whole, fixed-size and section chunkers |
| `DocumentSearch/LibraryChunker.cs` | `MarkdownReader` and `HeaderChunker` from DataIngestion |
| `DocumentSearch/ChunkIndex.cs` | In-memory vector search over chunks |
| `DocumentSearch/DocAssistant.cs` | Numbered prompt, answer, citation parsing |
| `Evaluation/RetrievalEval.cs` | Tuned and held-out sets, recall@k |
| `Evaluation/AnswerEval.cs` | Refusal and required-word checks for product answers |
| `Evaluation/JudgeEval.cs` | Groundedness and completeness scored by a judge model |
| `Evaluation/DocEval.cs` | Document answers, with checks on the cited section |
| `Evaluation/ExtractEval.cs` | Checks kind, codes, category and quantity for 15 questions |
| `Shared/VectorMath.cs` | Hand-written cosine similarity, kept for comparison |
| `docs/*.md` | The four shop documents |
| `uses.json` | Cached output of the enricher |
| `ShopMcpServer/Program.cs` | MCP server: stdio transport, `ShopMcpTools` adapter over `ShopTools` |
| `SemanticSearchDemo.slnx` | Both projects, so one build updates the server too |

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
| `hybrid` | Add keyword search and fuse it with vector results |
| `debug` | In chat, print the exact prompt sent to the model |
| `pg` | Use PostgreSQL instead of the in-memory index |
| `docs` | Run the document evaluation, then exit |
| `own` | With `docs`: use the hand-written section chunker |
| `fixed` / `whole` | With `docs`: cut every 40 words / keep whole documents |
| `show-chunks` | With `docs`: print every chunk before the evaluation |
| `tools` | Let the assistant look up price and stock |
| `trace` | With `tools`: print every message sent to the model |
| `no-route` | With `tools`: offer tools on every question, for comparison |
| `tooleval` | Run the tool evaluation, then exit |
| `guardtest` | Check the answer guard on fixed examples, then exit |
| `structured` | With `tools`: the model extracts the request, code writes the answer |
| `extract` | Run the extraction evaluation, then exit |
| `mcp` | With `tools` or `tooleval`: take the tools from `ShopMcpServer` |

`dotnet run -- eval no-enrich no-rerank` reproduces the plain vector-search baseline in about
a second. With reranking on, every question costs a model call, so an evaluation run takes
10 to 15 minutes on a machine without a GPU.

To regenerate the enrichment text after changing its prompt, delete `uses.json`.

## Limits (on purpose)

This is a learning project, not production code:

- The question sets are small (26, 19 and 20), so one question moves a score by several points.
- Some expected answers are debatable ("the stove won't light" expects the gas canister; the
  stove is a fair reply), and only one person labelled them.
- The held-out set has now informed three decisions (enrichment, reranking, hybrid), so it is
  no longer entirely untouched. A further round of changes would need fresh questions.
- Reranking uses a general chat model, which is far too slow for a live system.
- Each question costs two chat-model calls, one to rerank and one to answer.
- Answer quality is checked by required words, which cannot tell a good answer from a poor one
  that contains the right word. The judge model meant to cover this was not reliable.
- The judge evaluation still uses questions written for the 7-product catalogue.
- The enrichment cache is keyed by product id only, so a prompt change needs a manual delete.
- Every start re-embeds all products, even when nothing has changed.
- There is no vector index, so the search compares the question with every row.
- Every question returns three products, even an unrelated one, because there is no minimum score.
- Each question stands alone; there is no conversation history.
- The documents are short, so every chunking method answered all 15 questions; the
  differences show in separation and cost, not accuracy.
- Only three document questions are unanswerable, so the separation gap is a hint for a
  minimum score, not a setting.
- Products and documents are searched separately.
- DataIngestion is a preview package and forced newer versions of Microsoft.Extensions.AI
  and System.Numerics.Tensors.
- Price and stock come from an in-memory dictionary, not a real service. `FindProducts`
  matches on product names and reads the code out of the description with a regex.
- With tools on, a price question costs four model calls: rerank, router, request and answer.
- In tool-calling mode, the guard cannot catch an answer that contradicts a tool without a
  wrong number. Structured mode avoids this, at the cost of template-sounding answers.
- The tool loop has no cap on the number of rounds.
- The MCP server uses stdio only. A shared server would use HTTP, with each request carrying
  the caller's identity.


## Next steps

- Move from one-line products to real documents, which need chunking.
- Add need-style questions to the answer evaluation, to measure the retrieval gain end to end.
- Repeat the judge evaluation with a stronger model.
- Extract `MaxPrice` and `ProductName` so structured answers can say "yes, under €10" and "we don't sell kayaks".
- Cap the tool loop and log every tool call and result.
