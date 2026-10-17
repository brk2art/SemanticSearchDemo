// A question, the product codes the tool must be called with (empty: no call needed),
// and words a correct answer must contain. "a|b" means a or b.
public record ToolCase(string Question, string[] ExpectedCodes, string[] MustMention);

public static class ToolEval
{

    // The questions the tool rule and the router were written against.
    public static readonly ToolCase[] Tuned =
    [
        // One lookup needed.
        new("how much is the family tent?", ["TN-400"], ["349"]),
        new("is the sleeping bag in stock?", ["SB-300"], ["out of stock|not in stock"]),
        new("how much does the headlamp cost?", ["HL-050"], ["34.95"]),
        new("do you have the ultralight tent in stock?", ["TN-100"], ["out of stock|not in stock"]),
        new("what does a gas canister cost?", ["GC-420"], ["6.95"]),
        new("how many water filters do you have?", ["WF-070"], ["6"]),

        // Two lookups needed.
        new("which is cheaper, the lantern or the flashlight?", ["LN-060", "FL-055"], ["flashlight", "27.95"]),
        new("what do the stove and a gas canister cost together?", ["CS-400", "GC-420"], ["56.90"]),
        new("is the summer sleeping bag cheaper than the sleeping bag?", ["SB-310", "SB-300"], ["59.95", "99"]),

        // No lookup needed.
        new("what is the family tent for?", [], ["four|family"]),
        new("which tent is best for one person?", [], ["ultralight"]),
        new("what is your return policy?", [], ["I don't have that information"]),
    ];

    // Written after the router. Different wording, so it shows whether the router generalises.
    public static readonly ToolCase[] HeldOut =
    [
        // Needs a lookup, but avoids the words "price", "cost" and "stock".
        new("what's the lantern going for?", ["LN-060"], ["39.95"]),
        new("can I buy a summer sleeping bag today?", ["SB-310"], ["5|in stock|available"]),
        new("is the insect repellent under 10 euros?", ["IR-710"], ["8.95"]),
        new("how much would two gas canisters be?", ["GC-420"], ["13.90"]),
        new("which tent is the most expensive?", ["TN-100", "TN-200", "TN-400"], ["family", "349"]),

        // Sounds like a price question, but nothing to look up: we don't sell kayaks.
        new("how much is a kayak?", [], ["I don't have that information"]),

        // No lookup needed.
        new("what can I use to cook at the campsite?", [], ["stove"]),
        new("is the flashlight good for long distances?", [], ["long-range|long range|beam"]),
        new("what should I bring against mosquitoes?", [], ["repellent"]),
        new("how long does delivery take?", [], ["I don't have that information"]),
    ];

    private static bool Has(string text, string alternatives) =>
        alternatives.Split('|').Any(alt => text.Contains(alt, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(ShopAssistant assistant, ShopTools tools)
    {
        await RunSetAsync("Tuned", Tuned, assistant, tools);
        await RunSetAsync("Held out", HeldOut, assistant, tools);
    }

    private static async Task RunSetAsync(string name, ToolCase[] cases, ShopAssistant assistant, ShopTools tools)
    {
        int rightCalls = 0, missedCases = 0, extraCases = 0, correct = 0, totalCalls = 0;

        Console.WriteLine($"\n{name} tool questions ({cases.Length})");
        foreach (var c in cases)
        {
            tools.Calls.Clear();
            var reply = await assistant.AskAsync(c.Question);

            var called = tools.Calls.Distinct().ToList();
            var missing = c.ExpectedCodes.Except(called).ToList();
            var unexpected = called.Except(c.ExpectedCodes).ToList();
            totalCalls += tools.Calls.Count;

            var callsOk = missing.Count == 0 && unexpected.Count == 0;
            var answerOk = c.MustMention.All(m => Has(reply.Answer, m));

            if (callsOk) rightCalls++;
            if (missing.Count > 0) missedCases++;
            if (unexpected.Count > 0) extraCases++;
            if (callsOk && answerOk) correct++;

            var verdict = !callsOk ? (missing.Count > 0 ? "MISSED" : "EXTRA")
                        : !answerOk ? "ANSWER"
                        : "ok";

            Console.WriteLine($"\n  {verdict,-7} {c.Question}");
            Console.WriteLine($"          expected: {(c.ExpectedCodes.Length == 0 ? "(no call)" : string.Join(", ", c.ExpectedCodes))}");
            Console.WriteLine($"          called:   {(tools.Calls.Count == 0 ? "(no call)" : string.Join(", ", tools.Calls))}");
            Console.WriteLine($"          answer:   {reply.Answer}");
        }

        Console.WriteLine($"\n  ---- {name} ----");
        Console.WriteLine($"  Right tool calls:            {rightCalls} of {cases.Length}");
        Console.WriteLine($"  Missed a needed call:        {missedCases}");
        Console.WriteLine($"  Called when not needed:      {extraCases}");
        Console.WriteLine($"  Correct answers:             {correct} of {cases.Length}");
        Console.WriteLine($"  Tool calls in total:         {totalCalls} (minimum needed: {cases.Sum(c => c.ExpectedCodes.Length)})");
    }
}