// A correct answer for code-brackets. It shows that the tests can pass.

public static class Solution
{
    public static bool IsBalanced(string s)
    {
        var open = new Stack<char>();
        foreach (char c in s)
        {
            if (c == '(' || c == '[' || c == '{')
            {
                open.Push(c);
            }
            else if (c == ')' || c == ']' || c == '}')
            {
                char expected = c == ')' ? '(' : c == ']' ? '[' : '{';
                if (open.Count == 0 || open.Pop() != expected)
                {
                    return false;
                }
            }
        }
        return open.Count == 0;
    }
}
