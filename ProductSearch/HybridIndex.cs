using System.Text.RegularExpressions;

// Runs keyword search (BM25) next to the inner vector search
// and merges the two rankings with reciprocal rank fusion.
public class HybridIndex(IProductIndex inner, int pool = 50) : IProductIndex
{
    private const double K1 = 1.2, B = 0.75;   // standard BM25 settings
    private const int RrfK = 60;               // standard RRF damping constant

    // Words too common to tell products apart.
    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "the", "to", "for", "in", "on", "of", "and", "or", "is", "it",
        "i", "my", "me", "you", "do", "that", "with", "what", "how", "so",
    ];

    private readonly Dictionary<int, Product> _products = [];
    private readonly Dictionary<int, string[]> _tokens = [];
    private readonly Dictionary<string, int> _docFrequency = [];
    private double _averageLength;

    public async Task AddRangeAsync(IReadOnlyList<Product> products)
    {
        await inner.AddRangeAsync(products);

        // Keyword side of the write path: split each product into words and count them.
        foreach (var p in products)
        {
            var tokens = Tokenize($"{p.Name} {p.Description}");
            _products[p.Id] = p;
            _tokens[p.Id] = tokens;

            foreach (var term in tokens.Distinct())
                _docFrequency[term] = _docFrequency.GetValueOrDefault(term) + 1;
        }

        _averageLength = _tokens.Values.Average(t => t.Length);
    }

    public async Task<List<(Product Product, float Score)>> SearchAsync(string query, int top = 3)
    {
        var byVector = await inner.SearchAsync(query, pool);
        var byKeyword = KeywordSearch(query);

        // Reciprocal rank fusion: each list contributes 1 / (60 + rank).
        var fused = new Dictionary<int, double>();
        for (int i = 0; i < byVector.Count; i++)
            fused[byVector[i].Product.Id] = 1.0 / (RrfK + i + 1);
        for (int i = 0; i < byKeyword.Count; i++)
            fused[byKeyword[i]] = fused.GetValueOrDefault(byKeyword[i]) + 1.0 / (RrfK + i + 1);

        var vectorById = byVector.ToDictionary(r => r.Product.Id);

        return fused
            .OrderByDescending(f => f.Value)
            .Take(top)
            .Select(f => vectorById.TryGetValue(f.Key, out var hit)
                ? hit
                : (Product: _products[f.Key], Score: 0f))
            .ToList();
    }

    // Product ids that share at least one word with the query, best match first.
    private List<int> KeywordSearch(string query)
    {
        var terms = Tokenize(query).Distinct().ToArray();

        return _tokens
            .Select(doc => (Id: doc.Key, Score: Bm25(terms, doc.Value)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Id)
            .ToList();
    }

    // BM25: rare words count for more; repeats of a word give diminishing returns;
    // long texts are not favoured just for being long.
    private double Bm25(string[] queryTerms, string[] docTokens)
    {
        double score = 0;
        foreach (var term in queryTerms)
        {
            var tf = docTokens.Count(t => t == term);
            if (tf == 0) continue;

            var df = _docFrequency[term];
            var idf = Math.Log(1 + (_products.Count - df + 0.5) / (df + 0.5));
            score += idf * tf * (K1 + 1) / (tf + K1 * (1 - B + B * docTokens.Length / _averageLength));
        }
        return score;
    }

    private static string[] Tokenize(string text) =>
        Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+")
            .Select(m => m.Value)
            .Where(word => !StopWords.Contains(word))
            .ToArray();
}