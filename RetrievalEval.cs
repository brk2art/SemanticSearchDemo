// One test case: a question, and the ids of the products that should be found for it.
public record EvalCase(string Question, int[] ExpectedIds);

public static class RetrievalEval
{
    // Used while tuning the model, the text and the enrichment prompt.
    // Its score is optimistic: every failure here has been looked at and worked on.
    public static readonly EvalCase[] Tuned =
    [
        new("do you sell anything for rain?", [1, 2, 11]),
        new("what can I use to cook outdoors?", [6, 18]),
        new("do you have anything to keep drinks cold?", [7, 21]),
        new("I need to see in the dark", [5, 16, 17]),
        new("what should I wear on my feet for a wet hike?", [2]),
        new("something warm to sleep in", [4]),
        new("I want hot food on the trail", [6]),
        new("my hands need to be free while I walk at night", [5]),
        new("a tent for my whole family", [8]),
        new("the lightest tent you have", [9]),
        new("sleeping bag for summer", [14]),
        new("light for the whole tent", [16]),
        new("bag for a one-day walk", [24]),
        new("shoes for running", [12]),
        new("TN-400", [8]),
        new("do you have SB-310?", [14]),
        new("what is FL-055?", [17]),
        new("a jacket that is not waterproof", [10]),
        new("keep my coffee hot all day", [20]),
        new("is river water safe to drink?", [22]),
        new("my knees hurt going downhill", [25]),
        new("how do I keep mosquitoes away?", [27]),
        new("I got a small cut on my hand", [26]),
        new("I don't want to get lost without phone signal", [29]),
        new("fuel for my stove", [19]),
        new("what do I sleep on so the ground isn't cold?", [15]),
    ];

    // Never used for tuning. Do not change the system to make one of these pass
    // without adding fresh questions afterwards.
    public static readonly EvalCase[] HeldOut =
    [
        new("my back hurts from sleeping on the ground", [15]),
        new("my phone died and I need to find north", [29]),
        new("I burn easily in the sun", [28]),
        new("something to carry my lunch and a jacket on a short hike", [24]),
        new("I need to open a can and tighten a screw", [30]),
        new("ticks are everywhere on this trail", [27]),
        new("my child fell and scraped her knee", [26]),
        new("we ran out of clean water at the campsite", [22]),
        new("I want cold beer at the beach tomorrow", [21]),
        new("tea that stays warm on a winter walk", [20]),
        new("I need light but both hands to climb", [5]),
        new("crossing a stream without ruining my shoes", [13]),
        new("sleeping outside in July", [14]),
        new("camping alone and counting every gram", [9]),
        new("four of us need to sleep under one roof", [8]),
        new("it's freezing but dry, what do I wear?", [10]),
        new("carrying gear for two nights", [23]),
        new("the stove won't light, is it out of gas?", [19]),
        new("something to boil pasta in", [18]),
    ];

    public static async Task RunAsync(IProductIndex index)
    {
        await RunSetAsync(index, "Tuned set", Tuned);
        await RunSetAsync(index, "Held-out set", HeldOut);
    }

    private static async Task RunSetAsync(IProductIndex index, string name, EvalCase[] cases)
    {
        Console.WriteLine($"\n{name} ({cases.Length} questions)");

        foreach (var k in new[] { 1, 3, 10 })
        {
            double total = 0;
            var misses = new List<string>();

            foreach (var c in cases)
            {
                var results = await index.SearchAsync(c.Question, top: k);
                var foundIds = results.Select(r => r.Product.Id).ToHashSet();

                // Of the products that should be found, what share was in the top k?
                var recall = c.ExpectedIds.Count(foundIds.Contains) / (double)c.ExpectedIds.Length;
                total += recall;

                if (recall < 1)
                {
                    var got = string.Join(", ", results.Take(3).Select(r => $"{r.Product.Name} ({r.Score:F3})"));
                    misses.Add($"      {recall:F2}  {c.Question}\n            got: {got}");
                }
            }

            Console.WriteLine($"  Recall@{k}: {total / cases.Length:F2}");
            foreach (var miss in misses)
                Console.WriteLine(miss);
        }
    }
}