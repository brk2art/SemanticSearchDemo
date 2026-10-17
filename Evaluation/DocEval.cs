// A question, the file that holds the answer (null if no document does),
// and words a correct answer must contain. "a|b" means a or b.
public record DocCase(string Question, string? Source, string[] MustMention);

public static class DocEval
{
    public static readonly DocCase[] Cases =
    [
        new("how many days do I have to return something?", "returns.md", ["30"]),
        new("can I return an opened sunscreen?", "returns.md", ["cannot|can't|not"]),
        new("when will I get my refund?", "returns.md", ["5|five"]),
        new("how much is express delivery?", "delivery.md", ["9.95"]),
        new("can gas canisters be sent by express delivery?", "delivery.md", ["cannot|can't|not|only"]),
        new("how long does delivery to other EU countries take?", "delivery.md", ["10"]),
        new("how long is the warranty on tent seams?", "warranty.md", ["five|5"]),
        new("is the waterproof coating covered by the warranty?", "warranty.md", ["not|wear"]),
        new("my stove won't light, what should I do?", "stove-manual.md", ["canister|valve"]),
        new("can I cook inside my tent?", "stove-manual.md", ["carbon monoxide|never|not"]),
        new("my stove has a yellow flame", "stove-manual.md", ["clean|dirty|brush"]),
        new("which gas canisters fit the CS-400 stove?", "stove-manual.md", ["GC-420|EN 417|screw"]),

        // No document answers these.
        new("do you offer gift wrapping?", null, []),
        new("can I pay in instalments?", null, []),
        new("how do I clean my tent?", null, []),
    ];

    public static async Task RunAsync(DocAssistant assistant, int top)
    {
        int rightDoc = 0, correct = 0, totalWords = 0;

        Console.WriteLine($"\nDocument questions ({Cases.Length}), top {top} chunk(s) each");
        foreach (var c in Cases)
        {
            var reply = await assistant.AskAsync(c.Question, top);
            var refused = reply.Answer.Contains("I don't have that information", StringComparison.OrdinalIgnoreCase);

            var ok = c.Source is null
                ? refused
                : !refused && c.MustMention.All(m => m.Split('|')
                    .Any(alt => reply.Answer.Contains(alt, StringComparison.OrdinalIgnoreCase)));

            if (c.Source is not null && reply.Results[0].Chunk.Source == c.Source) rightDoc++;
            if (ok) correct++;
            totalWords += reply.WordsSent;

            Console.WriteLine($"\n  {(ok ? "ok" : "WRONG"),-6} {c.Question}");
            Console.WriteLine($"         found:  {string.Join(", ", reply.Results.Select(r => $"{r.Chunk.Source} / {r.Chunk.Section} ({r.Score:F3})"))}");
            Console.WriteLine($"         answer: {reply.Answer}");
        }

        Console.WriteLine("\n  ----");
        Console.WriteLine($"  Right document first:   {rightDoc} of {Cases.Count(c => c.Source is not null)}");
        Console.WriteLine($"  Correct answers:        {correct} of {Cases.Length}");
        Console.WriteLine($"  Words sent per question: {totalWords / Cases.Length}");
    }
}