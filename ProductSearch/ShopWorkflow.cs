using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

// Step 1's decision: what this goal needs.
public record NeedsPlan(
    [property: Description("The kinds of product this specific goal needs from the shop, each in a few words, " +
                           "for example 'rain protection'. Leave out anything the customer already has.")] string[] Needs,
    [property: Description("How many people the goal is for. 1 if the customer does not say.")] int People);

// Step 3's decision: one product per need.
public record Pick(
    [property: Description("The need this product is for, copied from the list of needs")] string Need,
    [property: Description("The product code, copied from that need's candidates")] string Code,
    [property: Description("How many to buy")] int Quantity);

public record Picks(
    [property: Description("At most one product per need, chosen from that need's candidates. " +
                           "Leave a need out if no candidate fits it.")] Pick[] Items);

// What one run produced, in a form both the workflow and the agent can return.
public record Outcome(string Answer, List<ListItem> Accepted, string? FinalList, string Stats);

// Plan and execute: the model makes three small decisions; code runs every step in between.
public class ShopWorkflow(IProductIndex index, ShopTools shop, IChatClient chat)
{
    private const int MaxNeeds = 6;
    private static readonly Regex CodePattern = new(@"Code ([A-Z]{2}-\d{3})");

    // The schemas are pasted into the prompts too: the model can't read the format it's constrained to (6b).
    private static readonly string PlanSchema = AIJsonUtilities.CreateJsonSchema(typeof(NeedsPlan)).ToString();
    private static readonly string PicksSchema = AIJsonUtilities.CreateJsonSchema(typeof(Picks)).ToString();

    public async Task<Outcome> RunAsync(string goal, CancellationToken ct = default)
    {
        // Step 1 (model): which needs does this goal have?
        var planResponse = await chat.GetResponseAsync<NeedsPlan>(
        [
            new(ChatRole.System, $"""
                You plan purchases for customers of an outdoor equipment shop.
                Read the customer's goal and list only the kinds of product this specific goal needs.
                A day trip needs different things from a night outdoors. Leave out what the customer already has.
                If the customer asks for a specific product, or asks whether the shop sells something,
                list that product as a need, in the customer's words.
                Reply with JSON that follows this schema:
                {PlanSchema}
                """),
            new(ChatRole.User, goal),
        ], cancellationToken: ct);

        var plan = planResponse.TryGetResult(out var planned) ? planned : new NeedsPlan([], 1);
        var needs = (plan.Needs ?? []).Select(n => n.Trim()).Where(n => n != "").Distinct().Take(MaxNeeds).ToList();
        var people = Math.Clamp(plan.People, 1, 10);
        Console.WriteLine($"  [plan] people {people}, needs: {(needs.Count == 0 ? "(none)" : string.Join(", ", needs))}");

        // Step 2 (code): exactly one search per need. No loops are possible.
        var candidates = new Dictionary<string, List<(StockItem Item, string Description)>>();
        foreach (var need in needs)
        {
            var results = await index.SearchAsync(need, 5);
            var items = new List<(StockItem, string)>();
            foreach (var r in results)
            {
                var code = CodePattern.Match(r.Product.Description).Groups[1].Value;
                if (code != "" && shop.Get(code) is { } item)
                    items.Add((item, r.Product.Description));
            }
            candidates[need] = items;
            Console.WriteLine($"  [search] {need} -> {string.Join(", ", items.Select(i => i.Item1.Name))}");
        }

        // Step 3 (model): one product per need, from that need's candidates. Stock is shown up front.
        var picks = new Picks([]);
        if (needs.Count > 0)
        {
            var options = string.Join("\n\n", candidates.Select(kv =>
                $"Need: {kv.Key}\n" + string.Join("\n", kv.Value.Select(c =>
                    $"- {c.Item.Code} {c.Item.Name}: {c.Description} " +
                    (c.Item.Stock == 0 ? "[OUT OF STOCK]" : $"[{c.Item.Stock} in stock]")))));

            var pickResponse = await chat.GetResponseAsync<Picks>(
            [
                new(ChatRole.System, $"""
                    You choose products for a customer of an outdoor equipment shop.
                    For each need, choose at most one product from that need's candidates, or none if no candidate fits.
                    Never choose a product marked OUT OF STOCK.
                    Quantity: one per person for personal items such as sleeping bags, sleeping mats and rain jackets;
                    otherwise one.
                    Reply with JSON that follows this schema:
                    {PicksSchema}
                    """),
                new(ChatRole.User, $"Goal: {goal}\nPeople: {people}\n\n{options}"),
            ], cancellationToken: ct);

            if (pickResponse.TryGetResult(out var picked))
                picks = picked;
        }

        // Step 4 (code): check every pick and work out the prices.
        var accepted = new List<ListItem>();
        var lines = new List<string>();
        var notes = new List<string>();
        decimal total = 0;

        foreach (var pick in picks.Items ?? [])
        {
            var code = (pick.Code ?? "").Trim().ToUpperInvariant();
            var item = candidates.Values.SelectMany(v => v).Select(c => c.Item).FirstOrDefault(i => i.Code == code);

            if (item is null)
            {
                notes.Add($"{code} was not among the candidates and was dropped.");
                continue;
            }
            if (accepted.Any(a => a.Code == code))
                continue;   // the same product chosen for two needs: once is enough

            var quantity = Math.Clamp(pick.Quantity, 1, 10);
            if (item.Stock < quantity)
            {
                notes.Add(item.Stock == 0
                    ? $"{item.Name} ({code}) is out of stock."
                    : $"Only {item.Stock} of {item.Name} ({code}) are in stock.");
                continue;
            }

            accepted.Add(new ListItem(code, quantity));
            total += item.Price * quantity;
            lines.Add($"- {item.Name} ({code}): {quantity} x {Euro(item.Price)} = {Euro(item.Price * quantity)}");
        }

        var finalList = accepted.Count == 0 ? null : string.Join("\n", lines) + $"\nTotal: {Euro(total)}";
        Console.WriteLine($"  [check] {accepted.Count} accepted" + (notes.Count > 0 ? $"; {string.Join(" ", notes)}" : ""));

        // Step 5: the reply. Code writes every sentence that states a fact; the model adds one sentence of advice.
        var sentences = new List<string>();

        foreach (var (need, items) in candidates)
        {
            var top = items.Count > 0 ? items[0].Item : null;
            var chosen = items.Select(c => c.Item).FirstOrDefault(i => accepted.Any(x => x.Code == i.Code));

            if (top is not null && top.Stock == 0)
                sentences.Add(chosen is not null
                    ? $"The {top.Name} ({top.Code}) is out of stock, so the list has the {chosen.Name} ({chosen.Code}) instead."
                    : $"The {top.Name} ({top.Code}) is out of stock.");
            else if (chosen is null)
                sentences.Add($"We don't have anything suitable for \"{need}\".");
        }

        if (needs.Count == 0)
            sentences.Add("We couldn't work out what you need. Could you tell us a bit more about your plans?");

        var modelCalls = needs.Count > 0 ? 2 : 1;

        if (accepted.Count > 0)
        {
            var names = candidates.Values.SelectMany(v => v).Select(c => c.Item)
                .Where(i => accepted.Any(x => x.Code == i.Code)).Select(i => i.Name).Distinct();

            var advice = await chat.GetResponseAsync(
            [
                new(ChatRole.System, """
                    Write one sentence, under 25 words, on why these products suit the customer's goal.
                    No numbers, no prices, no stock levels, no offers or promises. Just the reason.
                    """),
                new(ChatRole.User, $"Goal: {goal}\nProducts: {string.Join(", ", names)}"),
            ], cancellationToken: ct);
            modelCalls++;

            // The one free-text sentence is checked like any model output: a number or a promise means it's dropped.
            var text = advice.Text.Trim();
            string[] promises = ["order", "reserve", "deliver", "pick up", "in stock", "available"];
            if (text != "" && !Regex.IsMatch(text, @"\d") &&
                !promises.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
                sentences.Insert(0, text);
            else
                Console.WriteLine($"  [guard] advice dropped: {text}");
        }

        return new Outcome(string.Join(" ", sentences), accepted, finalList,
            $"needs {needs.Count}, searches {needs.Count}, model calls {modelCalls}");
    }

    private static string Euro(decimal price) => "€" + price.ToString("F2", CultureInfo.InvariantCulture);
}