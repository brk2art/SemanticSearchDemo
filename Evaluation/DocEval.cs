// A question, the file and section that hold the answer (null if no document does),
// and words a correct answer must contain. "a|b" means a or b.
public record DocCase(string Question, string? Source, string? Section, string[] MustMention);

public static class DocEval
{
    public static readonly DocCase[] Cases =
    [
        new("how many days do I have to return something?", "returns.md", "Return window", ["30"]),
        new("can I return an opened sunscreen?", "returns.md", "Items that cannot be returned", ["cannot|can't|not"]),
        new("when will I get my refund?", "returns.md", "Refunds", ["5|five"]),
        new("how much is express delivery?", "delivery.md", "Express delivery", ["9.95"]),
        new("can gas canisters be sent by express delivery?", "delivery.md", "Restricted items", ["cannot|can't|not|only"]),
        new("how long does delivery to other EU countries take?", "delivery.md", "International delivery", ["10"]),
        new("how long is the warranty on tent seams?", "warranty.md", "Tents", ["five|5"]),
        new("is the waterproof coating covered by the warranty?", "warranty.md", "What is not covered", ["not|wear"]),
        new("my stove won't light, what should I do?", "stove-manual.md", "Troubleshooting|Lighting the stove", ["canister|valve"]),
        new("can I cook inside my tent?", "stove-manual.md", "Safety", ["carbon monoxide|never|not"]),
        new("my stove has a yellow flame", "stove-manual.md", "Troubleshooting", ["clean|dirty|brush"]),
        new("which gas canisters fit the CS-400 stove?", "stove-manual.md", "Before first use", ["GC-420|EN 417|screw"]),

        // No document answers these. The right reply is the refusal, with no citation.
        new("do you offer gift wrapping?", null, null, []),
        new("can I pay in instalments?", null, null, []),
        new("how do I clean my tent?", null, null, []),
    ];

    private static bool Has(string text, string alternatives) =>
        alternatives.Split('|').Any(alt => text.Contains(alt, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(DocAssistant assistant, int top)
    {
        int rightDoc = 0, correct = 0, rightCitation = 0, uncited = 0, invalid = 0, totalWords = 0;
        var answerable = Cases.Count(c => c.Source is not null);

        Console.WriteLine($"\nDocument questions ({Cases.Length}), top {top} chunk(s) each");
        foreach (var c in Cases)
        {
            var reply = await assistant.AskAsync(c.Question, top);
            var refused = Has(reply.Answer, "I don't have that information");

            // Did the answer point at a section that really holds the answer?
            var citedRight = c.Source is not null && reply.Cited.Any(ch =>
                ch.Source == c.Source && c.Section!.Split('|').Contains(ch.Section));

            var ok = c.Source is null
                ? refused && reply.Cited.Count == 0
                : !refused && c.MustMention.All(m => Has(reply.Answer, m)) && citedRight;


            if (c.Source is not null && reply.Results[0].Chunk.Source == c.Source) rightDoc++;
            if (citedRight) rightCitation++;
            if (c.Source is not null && !refused && reply.Cited.Count == 0) uncited++;
            invalid += reply.InvalidCitations;
            if (ok) correct++;
            totalWords += reply.WordsSent;

            Console.WriteLine($"\n  {(ok ? "ok" : "WRONG"),-6} {c.Question}");
            Console.WriteLine($"         found:  {string.Join(", ", reply.Results.Select(r => $"{r.Chunk.Source} / {r.Chunk.Section} ({r.Score:F3})"))}");
            Console.WriteLine($"         cited:  {(reply.Cited.Count == 0 ? "(none)" : string.Join(", ", reply.Cited.Select(ch => $"{ch.Source} / {ch.Section}")))}");
            Console.WriteLine($"         answer: {reply.Answer}");
        }

        Console.WriteLine("\n  ----");
        Console.WriteLine($"  Right document first:          {rightDoc} of {answerable}");
        Console.WriteLine($"  Cited the right section:       {rightCitation} of {answerable}");
        Console.WriteLine($"  Answers with no citation:      {uncited}");
        Console.WriteLine($"  Citations to missing numbers:  {invalid}");
        Console.WriteLine($"  Correct answers:               {correct} of {Cases.Length}");
        Console.WriteLine($"  Words sent per question:       {totalWords / Cases.Length}");
    }
}