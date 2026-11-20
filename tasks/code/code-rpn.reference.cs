// A correct answer for code-rpn. It shows that the tests can pass.

public static class Solution
{
    public static long EvalRpn(string expression)
    {
        var stack = new Stack<long>();
        foreach (string token in expression.Split(' '))
        {
            if (token is "+" or "-" or "*" or "/")
            {
                long b = stack.Pop();
                long a = stack.Pop();
                stack.Push(token switch
                {
                    "+" => a + b,
                    "-" => a - b,
                    "*" => a * b,
                    _ => a / b,
                });
            }
            else
            {
                stack.Push(long.Parse(token, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return stack.Pop();
    }
}
