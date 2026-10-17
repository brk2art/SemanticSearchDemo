using Microsoft.Extensions.AI;
using System.Text.RegularExpressions;

public record DocReply(
    string Answer, 
    List<(DocChunk Chunk, float Score)> Results, 
    List<DocChunk> Cited,   // chunks the answer points to
    int InvalidCitations,   // numbers that match no chunk, e.g. [7] when only 3 were sent 
    int WordsSent);

// The read path for documents: retrieve chunks, build a numbered prompt, generate, read the citations.
public class DocAssistant(ChunkIndex index, IChatClient chat)
{
    public const string Refusal = "I don't have that information.";

    public async Task<DocReply> AskAsync(string question, int top)
    {
        var results = await index.SearchAsync(question, top);

        // Number each chunk, so the answer can point to it: [1], [2], [3].
        var context = string.Join("\n\n", results.Select((r, i) =>
            $"[{i + 1}] {r.Chunk.Source} / {r.Chunk.Section}\n{r.Chunk.Text}"));

        var userMessage = $"""
            Shop documents:
            {context}

            Question: {question}

            Answer only from the documents above.

            If the documents contain the answer:
            - reply in one or two sentences,
            - put the number of the document after each fact, like [2],
            - add nothing else.

            If the documents do not contain the answer, reply with exactly this sentence and nothing else:
            {Refusal}
            """;

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You are a customer service assistant for an outdoor equipment shop."),
            new(ChatRole.User, userMessage),
        ];

        var response = await chat.GetResponseAsync(messages, new ChatOptions { Temperature = 0 });
        var answer = response.Text.Trim();

        // Read every [n] out of the answer and map it back to a chunk.
        var numbers = Regex.Matches(answer, @"\[(\d+)\]")
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .ToList();

        var cited = numbers
            .Where(n => n >= 1 && n <= results.Count)
            .Select(n => results[n - 1].Chunk)
            .ToList();

        var invalid = numbers.Count(n => n < 1 || n > results.Count);
        var wordsSent = context.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        return new DocReply(answer, results, cited, invalid, wordsSent);
    }
}