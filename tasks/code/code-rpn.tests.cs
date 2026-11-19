// Tests for code-rpn. They're built together with the reply's Solution class. Exit code 0 means every check passed.

int failures = 0;

void Check(string input, long expected)
{
    long actual = Solution.EvalRpn(input);
    if (actual != expected)
    {
        Console.WriteLine($"EvalRpn(\"{input}\"): expected {expected}, got {actual}");
        failures++;
    }
}

Check("3", 3);
Check("2 3 +", 5);
Check("10 3 - 2 -", 5);
Check("5 1 2 + 4 * + 3 -", 14);
Check("7 2 /", 3);
Check("-7 2 /", -3);
Check("2 -3 *", -6);
Check("4 2 5 * + 1 3 2 * + /", 2);
Check("100000 100000 *", 10000000000);

return failures == 0 ? 0 : 1;
