// A correct answer for code-top-words. It shows that the tests can pass.

public static class Solution
{
    public static string[] TopWords(string text, int k)
    {
        var counts = new Dictionary<string, int>();
        var word = new System.Text.StringBuilder();
        foreach (char c in text + " ")
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
            {
                word.Append(char.ToLowerInvariant(c));
            }
            else if (word.Length > 0)
            {
                string w = word.ToString();
                counts[w] = counts.GetValueOrDefault(w) + 1;
                word.Clear();
            }
        }
        return counts
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(k)
            .Select(p => p.Key)
            .ToArray();
    }
}
