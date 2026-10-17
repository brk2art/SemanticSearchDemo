// A piece of a document: which file it came from, which section, and the text.
public record DocChunk(string Source, string Section, string Text);
