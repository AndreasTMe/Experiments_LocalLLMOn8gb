// A correct answer for ex-code. It shows that the tests can pass.

public static class Solution
{
    public static string Compress(string s)
    {
        var result = new System.Text.StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            int run = 1;
            while (i + run < s.Length && s[i + run] == s[i])
            {
                run++;
            }
            result.Append(s[i]).Append(run);
            i += run;
        }
        return result.ToString();
    }
}
