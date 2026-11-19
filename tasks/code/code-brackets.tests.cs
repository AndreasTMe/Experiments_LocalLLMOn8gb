// Tests for code-brackets. They're built together with the reply's Solution class. Exit code 0 means every check passed.

int failures = 0;

void Check(string input, bool expected)
{
    bool actual = Solution.IsBalanced(input);
    if (actual != expected)
    {
        Console.WriteLine($"IsBalanced(\"{input}\"): expected {expected}, got {actual}");
        failures++;
    }
}

Check("", true);
Check("()", true);
Check("([]{})", true);
Check("{[()()]}", true);
Check("a(b[c]d)e", true);
Check("(]", false);
Check("([)]", false);
Check("((", false);
Check("))", false);
Check("}{", false);
Check("(()", false);
Check("x)", false);

return failures == 0 ? 0 : 1;
