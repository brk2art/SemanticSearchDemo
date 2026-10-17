public static class Chunker
{
    // Step 1: no chunking. Each document becomes one chunk.
    public static List<DocChunk> WholeDocuments(string folder) =>
        Directory.GetFiles(folder, "*.md")
            .Select(path => new DocChunk(Path.GetFileName(path), "(whole document)", File.ReadAllText(path)))
            .ToList();

    // Step 2: fixed-size chunks. Cut every N words, ignoring sentences and headings.
    public static List<DocChunk> FixedSize(string folder, int wordsPerChunk)
    {
        var chunks = new List<DocChunk>();

        foreach (var path in Directory.GetFiles(folder, "*.md"))
        {
            var words = File.ReadAllText(path)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < words.Length; i += wordsPerChunk)
            {
                var piece = string.Join(' ', words.Skip(i).Take(wordsPerChunk));
                var section = $"words {i + 1}-{Math.Min(i + wordsPerChunk, words.Length)}";
                chunks.Add(new DocChunk(Path.GetFileName(path), section, piece));
            }
        }

        return chunks;
    }

    // Step 3 and 4: one chunk per section.A section starts at a "## " heading.
    // With withContext, each chunk's text starts with "Document title > Section".
    public static List<DocChunk> ByHeadings(string folder, bool withContext = false)
    {
        var chunks = new List<DocChunk>();

        foreach (var path in Directory.GetFiles(folder, "*.md"))
        {
            string title = "";
            string? section = null;
            var lines = new List<string>();

            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("## "))
                {
                    AddSection();               // close the previous section
                    section = line[3..].Trim(); // start a new one
                    lines.Clear();
                }
                else if (line.StartsWith("# "))
                {
                    title = line[2..].Trim();   // the document title, kept for every chunk
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line.Trim());     // body text of the current section
                }
            }
            AddSection();                       // close the last section

            void AddSection()
            {
                if (section is null || lines.Count == 0) return;

                var body = string.Join(' ', lines);
                var text = withContext ? $"{title} > {section}\n{body}" : body;
                chunks.Add(new DocChunk(Path.GetFileName(path), section, text));
            }
        }

        return chunks;
    }
}