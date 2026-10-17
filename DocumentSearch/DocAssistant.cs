using Microsoft.Extensions.AI;

public record DocReply(string Answer, List<(DocChunk Chunk, float Score)> Results, int WordsSent);

// The read path for documents: retrieve chunks, build the prompt, generate.
public class DocAssistant(ChunkIndex index, IChatClient chat)
{
    public const string Refusal = "I don't have that information.";

    public async Task<DocReply> AskAsync(string question, int top)
    {
        var results = await index.SearchAsync(question, top);

        // Each chunk is labelled with where it came from.
        var context = string.Join("\n\n---\n\n",
            results.Select(r => $"[{r.Chunk.Source} / {r.Chunk.Section}]\n{r.Chunk.Text}"));

        var userMessage = $"""
            Shop documents:
            {context}

            Question: {question}

            Answer only from the documents above, in one or two sentences.
            If they do not state the answer, reply exactly: {Refusal}
            """;

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You are a customer service assistant for an outdoor equipment shop."),
            new(ChatRole.User, userMessage),
        ];

        var response = await chat.GetResponseAsync(messages, new ChatOptions { Temperature = 0 });

        // How much text the model had to read for this question.
        var wordsSent = context.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        return new DocReply(response.Text.Trim(), results, wordsSent);
    }
}