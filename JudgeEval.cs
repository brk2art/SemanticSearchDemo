using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using System;
using System.Collections.Generic;
using System.Text;

// One case: a question and the answer a knowledgeable person would give.
public record JudgeCase(string Question, string GroundTruth);

public static class JudgeEval
{
    public static readonly JudgeCase[] Cases =
    [
        new("do you sell anything for rain?", "Yes: the Rain jacket and the Waterproof boots."),
        new("what can I use to cook outdoors?", "The Camping stove."),
        new("how many people fit in the tent?", "Two people."),
        new("do you have anything to keep drinks cold?", "Yes: the Water bottle."),
        new("I need to see in the dark", "The Headlamp."),
        new("something warm to sleep in", "The Sleeping bag."),
    ];

    public static async Task RunAsync(ShopAssistant assistant, IChatClient judge)
    {
        // The model that does the grading.
        var config = new ChatConfiguration(judge);
        var groundedness = new GroundednessEvaluator();
        var completeness = new CompletenessEvaluator();

        Console.WriteLine($"\nJudged answers, 1 (poor) to 5 (excellent). {Cases.Length} questions, three model calls each.");
        
        foreach(var c in Cases)
        {
            var reply = await assistant.AskAsync(c.Question);

            // What the judge sees: the question, the answer, and reference material.
            List<ChatMessage> messages = [new(ChatRole.User, c.Question)];
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, reply.Answer));
            var retrieved = string.Join("\n", reply.Results.Select(r => $"{r.Product.Name}: {r.Product.Description}"));

            // Groundedness compares the answer with what was retrieved.
            var g = await groundedness.EvaluateAsync(messages, response, config, [new GroundednessEvaluatorContext(retrieved)]);

            // Completeness compares the answer with the expected answer.
            var k = await completeness.EvaluateAsync(messages, response, config, [new CompletenessEvaluatorContext(c.GroundTruth)]);

            var gm = g.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName);
            var km = k.Get<NumericMetric>(CompletenessEvaluator.CompletenessMetricName);

            Console.WriteLine($"\n  {c.Question}");
            Console.WriteLine($"    answer:       {reply.Answer}");
            Console.WriteLine($"    groundedness: {gm.Value?.ToString() ?? "no score"}  {gm.Reason}");
            Console.WriteLine($"    completeness: {km.Value?.ToString() ?? "no score"}  {km.Reason}");
        }
    }
}
