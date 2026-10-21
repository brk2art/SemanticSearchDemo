using System.Diagnostics;
using System.Text.RegularExpressions;

// A need the list must cover: at least MinQuantity items from any of these codes.
public record Need(string Name, string[] Codes, int MinQuantity = 1);

// A goal, and what a good outcome must satisfy.
// ExpectList: true = a list must be produced, false = none may be, null = either is fine.
public record AgentCase(string Goal, Need[] Needs, string[] Forbidden, bool? ExpectList = true,
    string[]? MustMention = null, string[]? MustNotMention = null);

public static class AgentEval
{
    private static readonly string[] Tents = ["TN-100", "TN-200", "TN-400"];
    private static readonly string[] SleepingBags = ["SB-300", "SB-310"];

    public static readonly AgentCase[] Cases =
    [
        // The goal the agent's instructions were written against.
        new("I'm going camping for two nights with my partner. I already have a tent. What else do I need from your shop, and what will it cost in total?",
            [
                new("sleeping bags", SleepingBags, 2),
                new("sleeping mats", ["SM-320"], 2),
                new("cooking", ["CS-400", "CP-410"]),
                new("light", ["LN-060", "HL-050", "FL-055"]),
            ],
            Forbidden: Tents),

        // A different trip with different needs: nothing for sleeping.
        new("I'm going on a day hike alone and rain is forecast. What should I buy?",
            [new("rain protection", ["RJ-100", "RP-110"])],
            Forbidden: [.. Tents, .. SleepingBags]),

        // Larger quantities, and this time a tent is needed.
        new("My family of four is camping for a weekend and we have no gear at all. What do we need?",
            [
                new("family tent", ["TN-400"]),
                new("sleeping bags", SleepingBags, 4),
                new("cooking", ["CS-400", "CP-410"]),
            ],
            Forbidden: []),

        // The requested product is out of stock. Suggesting an alternative is fine,
        // as long as the answer says plainly that the ultralight tent is out of stock.
        new("I need the ultralight tent for a solo trek. Can I buy it?",
            [],
            Forbidden: ["TN-100"],
            ExpectList: null,
            MustMention: ["out of stock", "not in stock", "unavailable", "not available"],
            MustNotMention: ["we have the ultralight", "place an order", "can order"]),

        // Nothing to sell: no list, and no invented products or services.
        new("Do you sell kayaks? I need one with a paddle.",
            [],
            Forbidden: [],
            ExpectList: false,
            MustMention: ["don't", "do not", "not sell", "no kayak", "not available"],
            MustNotMention: ["yes, we do sell", "we sell kayaks", "we do sell kayaks", "place an order", "can order"]),
    ];

    // run: turns a goal into an Outcome. The workflow and the agent both fit.
    public static async Task RunAsync(string name, Func<string, CancellationToken, Task<Outcome>> run)
    {
        int met = 0;
        var total = Stopwatch.StartNew();
        Console.WriteLine($"\n{name}");

        foreach (var c in Cases)
        {
            Console.WriteLine($"\n> {c.Goal}");
            var clock = Stopwatch.StartNew();

            // A time limit per goal. A run that exceeds it is a failure, not a crash.
            Outcome? outcome = null;
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                outcome = await run(c.Goal, limit.Token);
            }
            catch (OperationCanceledException)
            {
            }
            clock.Stop();

            var answer = outcome?.Answer ?? "";
            var accepted = outcome?.Accepted ?? [];
            var hasList = outcome?.FinalList is not null;
            var problems = new List<string>();

            if (outcome is null) problems.Add("timed out after 5 minutes");
            if (c.ExpectList == true && !hasList) problems.Add("no list was produced");
            if (c.ExpectList == false && hasList) problems.Add("a list was produced, but none was expected");

            foreach (var need in c.Needs)
            {
                var quantity = accepted.Where(i => need.Codes.Contains(i.Code)).Sum(i => i.Quantity);
                if (quantity < need.MinQuantity)
                    problems.Add($"{need.Name}: {quantity}, needs at least {need.MinQuantity}");
            }

            foreach (var code in c.Forbidden.Where(f => accepted.Any(i => i.Code == f)))
                problems.Add($"should not include {code}");

            if (c.MustMention is not null &&
                !c.MustMention.Any(m => answer.Contains(m, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"the answer should say one of: {string.Join(" / ", c.MustMention)}");

            if (c.MustNotMention is not null)
                foreach (var m in c.MustNotMention.Where(m => answer.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"the answer should not say: \"{m}\"");

            // Both designs are told not to state prices: code writes the list. A price in the text is invented.
            if (Regex.IsMatch(answer, @"[€$]\s?\d|\d+[.,]\d{2}"))
                problems.Add("the answer states a price itself");

            if (problems.Count == 0) met++;

            Console.WriteLine($"  {(problems.Count == 0 ? "ok" : "FAIL")}   {outcome?.Stats ?? "-"}, {clock.Elapsed.TotalSeconds:F0} s");
            foreach (var p in problems)
                Console.WriteLine($"        - {p}");
            Console.WriteLine($"  answer: {answer}");
            if (hasList)
                Console.WriteLine($"  list:\n{outcome!.FinalList}");
        }

        Console.WriteLine($"\n  ---- {name}: {met} of {Cases.Length} goals met, {total.Elapsed.TotalSeconds:F0} s ----");
    }
}