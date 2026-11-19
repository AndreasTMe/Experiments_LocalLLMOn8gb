// Tests for code-duration. They're built together with the reply's Solution class. Exit code 0 means every check passed.

int failures = 0;

void Check(string input, int expected)
{
    int actual = Solution.ParseDuration(input);
    if (actual != expected)
    {
        Console.WriteLine($"ParseDuration(\"{input}\"): expected {expected}, got {actual}");
        failures++;
    }
}

Check("1h30m15s", 5415);
Check("45s", 45);
Check("2h", 7200);
Check("90m", 5400);
Check("1h15s", 3615);
Check("0s", 0);
Check("007m", 420);
Check("", -1);
Check("1m1h", -1);
Check("1h1h", -1);
Check("1 h", -1);
Check("h", -1);
Check("10", -1);
Check("1d", -1);
Check("1H", -1);
Check("1h30", -1);

return failures == 0 ? 0 : 1;
