// A question, the product codes the tool must be called with (empty: no call needed),
// and words a correct answer must contain. "a|b" means a or b.
using System.Text.RegularExpressions;
using System.Diagnostics;

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
        new("do you have the ultralight tent in stock?", ["TN-100"], ["out of stock|not in stock|don't have the ultralight"]),
        new("what does a gas canister cost?", ["GC-420"], ["6.95"]),
        new("how many water filters do you have?", ["WF-070"], ["6"]),

        // Two lookups needed.
        new("which is cheaper, the lantern or the flashlight?", ["LN-060", "FL-055"], ["flashlight", "27.95"]),
        new("what do the stove and a gas canister cost together?", ["CS-400", "GC-420"], ["56.90"]),
        new("is the summer sleeping bag cheaper than the sleeping bag?", ["SB-310", "SB-300"], ["59.95|39.05", "99|39.05"]),

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
        new("which tent is the most expensive?", ["find:tent"], ["family", "349"]),

        // We don't sell kayaks: the right move is to check, then say so.
        new("how much is a kayak?", ["find:kayak"], ["don't sell|do not sell|no kayak|sells no|I don't have that information"]),

        // No lookup needed.
        new("what can I use to cook at the campsite?", [], ["stove"]),
        new("is the flashlight good for long distances?", [], ["long-range|long range|beam"]),
        new("what should I bring against mosquitoes?", [], ["repellent"]),
        new("how long does delivery take?", [], ["I don't have that information"]),
    ];

    private static bool Has(string text, string alternatives) =>
        alternatives.Split('|').Any(alt => text.Contains(alt, StringComparison.OrdinalIgnoreCase));

    // A price-like number: digits, a dot, two digits (349.00, 6.95, 56.90).
    private static readonly Regex Price = new(@"\d+\.\d{2}");

    public static async Task RunAsync(ShopAssistant assistant, ShopTools tools)
    {
        await RunSetAsync("Tuned", Tuned, assistant, tools);
        await RunSetAsync("Held out", HeldOut, assistant, tools);
    }

    private static async Task RunSetAsync(string name, ToolCase[] cases, ShopAssistant assistant, ShopTools tools)
    {
        var clock = Stopwatch.StartNew();

        int rightCalls = 0, missedCases = 0, extraCases = 0, brokenCases = 0, inventedCases = 0, correct = 0, totalCalls = 0;

        Console.WriteLine($"\n{name} tool questions ({cases.Length})");
        foreach (var c in cases)
        {
            tools.Calls.Clear();
            tools.Results.Clear();
            var reply = await assistant.AskAsync(c.Question);

            var called = tools.Calls.Distinct().ToList();
            var missing = c.ExpectedCodes.Except(called).ToList();
            var unexpected = called.Except(c.ExpectedCodes).ToList();
            totalCalls += tools.Calls.Count;

            // Tool-call text that leaked into the answer.
            var broken = reply.Answer.Contains('{') || reply.Answer.Contains("\"name\"");

            // Prices in the answer that no tool returned and that aren't an expected total.
            var allowed = tools.Results.Concat(c.MustMention)
                .SelectMany(t => Price.Matches(t).Select(m => m.Value))
                .ToHashSet();
            var invented = Price.Matches(reply.Answer)
                .Select(m => m.Value)
                .Where(n => !allowed.Contains(n))
                .Distinct()
                .ToList();

            var callsOk = missing.Count == 0 && unexpected.Count == 0;
            var answerOk = c.MustMention.All(m => Has(reply.Answer, m));

            if (callsOk) rightCalls++;
            if (missing.Count > 0) missedCases++;
            if (unexpected.Count > 0) extraCases++;
            if (broken) brokenCases++;
            if (invented.Count > 0) inventedCases++;
            if (callsOk && answerOk && !broken && invented.Count == 0) correct++;

            var verdict = !callsOk ? (missing.Count > 0 ? "MISSED" : "EXTRA")
                        : broken ? "BROKEN"
                        : invented.Count > 0 ? "INVENTED"
                        : !answerOk ? "ANSWER"
                        : "ok";

            Console.WriteLine($"\n  {verdict,-8} {c.Question}");
            Console.WriteLine($"           expected: {(c.ExpectedCodes.Length == 0 ? "(no call)" : string.Join(", ", c.ExpectedCodes))}");
            Console.WriteLine($"           called:   {(tools.Calls.Count == 0 ? "(no call)" : string.Join(", ", tools.Calls))}");

            if (reply.Guard != "ok")
                Console.WriteLine($"           guard:    {reply.Guard}");

            if (invented.Count > 0)
                Console.WriteLine($"           invented: {string.Join(", ", invented)}");
            Console.WriteLine($"           answer:   {reply.Answer}");
        }

        Console.WriteLine($"\n  ---- {name} ----");
        Console.WriteLine($"  Right tool calls:            {rightCalls} of {cases.Length}");
        Console.WriteLine($"  Missed a needed call:        {missedCases}");
        Console.WriteLine($"  Called when not needed:      {extraCases}");
        Console.WriteLine($"  Tool text in the answer:     {brokenCases}");
        Console.WriteLine($"  Invented prices:             {inventedCases}");
        Console.WriteLine($"  Correct answers:             {correct} of {cases.Length}");
        Console.WriteLine($"  Tool calls in total:         {totalCalls} (minimum needed: {cases.Sum(c => c.ExpectedCodes.Length)})");
        Console.WriteLine($"  Time:                        {clock.Elapsed.TotalSeconds:F0} s");
    }
}