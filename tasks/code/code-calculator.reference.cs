// A correct answer for code-calculator. It shows that the tests can pass.

public static class Solution
{
    public static long Evaluate(string expression)
    {
        string s = expression.Replace(" ", "");
        int pos = 0;
        return ParseSum(s, ref pos);
    }

    private static long ParseSum(string s, ref int pos)
    {
        long value = ParseProduct(s, ref pos);
        while (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
        {
            char op = s[pos++];
            long right = ParseProduct(s, ref pos);
            value = op == '+' ? value + right : value - right;
        }
        return value;
    }

    private static long ParseProduct(string s, ref int pos)
    {
        long value = ParseUnary(s, ref pos);
        while (pos < s.Length && (s[pos] == '*' || s[pos] == '/'))
        {
            char op = s[pos++];
            long right = ParseUnary(s, ref pos);
            value = op == '*' ? value * right : value / right;
        }
        return value;
    }

    private static long ParseUnary(string s, ref int pos)
    {
        if (s[pos] == '-')
        {
            pos++;
            return -ParseUnary(s, ref pos);
        }
        if (s[pos] == '(')
        {
            pos++;
            long value = ParseSum(s, ref pos);
            pos++; // The closing parenthesis.
            return value;
        }
        long number = 0;
        while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9')
        {
            number = number * 10 + (s[pos] - '0');
            pos++;
        }
        return number;
    }
}
