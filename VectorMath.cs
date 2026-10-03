public static class VectorMath
{
    // Cosine similarity: close to 1 when two vectors point the same way (similar meaning),
    // close to 0 when they are unrelated.
    public static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same number of dimensions.");

        float dot = 0, lenA = 0, lenB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            lenA += a[i] * a[i];
            lenB += b[i] * b[i];
        }

        return dot / (MathF.Sqrt(lenA) * MathF.Sqrt(lenB));
    }
}