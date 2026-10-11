// One test case: a question, whether the data can answer it,
// and words a correct answer must contain.
public record AnswerCase(string Question, bool Answerable, string[] MustMention);

public static class AnswerEval
{
    public static readonly AnswerCase[] Cases =
    [
        // The data can answer these.
        new("do you sell anything for rain?", true, ["jacket", "boots", "poncho"]),
        new("what can I use to cook outdoors?", true, ["stove"]),
        new("how many people fit in the family tent?", true, ["four"]),
        new("which tent is best for one person?", true, ["ultralight"]),
        new("do you have anything to keep drinks cold?", true, ["bottle", "cooler"]),
        new("I need to keep my coffee hot all day", true, ["thermos"]),
        new("what is the code of the flashlight?", true, ["FL-055"]),
        new("what is in the first aid kit?", true, ["bandages"]),
        new("my knees hurt going downhill", true, ["poles"]),
        new("is river water safe to drink?", true, ["filter"]),

        // The data cannot answer these. The only correct reply is the refusal.
        new("how much is the family tent?", false, []),
        new("is the rain jacket available in red?", false, []),
        new("which is cheaper, the lantern or the flashlight?", false, []),
        new("what is your return policy?", false, []),
        new("is the camping stove in stock?", false, []),
        new("how heavy is the sleeping bag?", false, []),
        new("what sizes do the boots come in?", false, []),
        new("does the headlamp need batteries?", false, []),
        new("can you deliver the cooler box tomorrow?", false, []),
        new("is the water bottle dishwasher safe?", false, []),
    ];

    public static async Task RunAsync(ShopAssistant assistant)
    {
        int notRefused = 0, overRefused = 0, missing = 0;

        Console.WriteLine("\nAnswers");
        foreach (var c in Cases)
        {
            var reply = await assistant.AskAsync(c.Question);
            var refused = reply.Answer.Contains("I don't have that information", StringComparison.OrdinalIgnoreCase);

            string verdict;
            if (!c.Answerable)
                verdict = refused ? "ok" : "NOT-REFUSED";
            else if (refused)
                verdict = "OVER-REFUSED";
            else if (!c.MustMention.All(m => reply.Answer.Contains(m, StringComparison.OrdinalIgnoreCase)))
                verdict = "MISSING";
            else
                verdict = "ok";

            if (verdict == "NOT-REFUSED") notRefused++;
            if (verdict == "OVER-REFUSED") overRefused++;
            if (verdict == "MISSING") missing++;

            Console.WriteLine($"  {verdict,-12}  {c.Question}");
            Console.WriteLine($"                -> {reply.Answer}");
        }

        var answerable = Cases.Count(c => c.Answerable);
        var unanswerable = Cases.Length - answerable;

        Console.WriteLine("  ----");
        Console.WriteLine($"  Answered when it should refuse: {notRefused} of {unanswerable}");
        Console.WriteLine($"  Refused when it should answer:  {overRefused} of {answerable}");
        Console.WriteLine($"  Answered but missed the point:  {missing} of {answerable}");
    }
}
