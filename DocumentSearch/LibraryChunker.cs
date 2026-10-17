using Microsoft.Extensions.DataIngestion;
using Microsoft.ML.Tokenizers;

public static class LibraryChunker
{
    public static async Task<List<DocChunk>> ByHeadingsAsync(string folder)
    {
        // The tokenizer is only used to count tokens for the size limit.
        // It belongs to a different model family from ours, so counts are approximate,
        // which is fine for a limit.
        var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");

        var chunker = new HeaderChunker(new IngestionChunkerOptions(tokenizer)
        {
            MaxTokensPerChunk = 200, // our sections are far below this, so none is split
            OverlapTokens = 0
        });

        var reader = new MarkdownReader();
        var chunks = new List<DocChunk>();

        foreach (var path in Directory.GetFiles(folder, "*.md"))
        {
            var name = Path.GetFileName(path);

            // Read: Markdown text becomes a structured document (headers, paragraphs, tables).
            IngestionDocument document = await reader.ReadAsync(new FileInfo(path), name);

            // Chunk: one chunk per section, with the heading path written at the start.
            await foreach (var chunk in chunker.ProcessAsync(document))
            {
                // Context holds the heading path as Markdown, for example "# Warranty ## Tents".
                // The section name is the last heading in it.
                var section = (chunk.Context ?? "")
                    .Split('#', StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault()?.Trim() ?? "";

                chunks.Add(new DocChunk(name, section, chunk.Content));
            }
        }

        return chunks;
    }
}
