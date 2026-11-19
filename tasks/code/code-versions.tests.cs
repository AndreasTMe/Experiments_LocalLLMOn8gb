// Tests for code-versions. They're built together with the reply's Solution class. Exit code 0 means every check passed.

int failures = 0;

void Check(string a, string b, int expected)
{
    int actual = Solution.CompareVersions(a, b);
    if (actual != expected)
    {
        Console.WriteLine($"CompareVersions(\"{a}\", \"{b}\"): expected {expected}, got {actual}");
        failures++;
    }
}

Check("1.0", "1.0", 0);
Check("1.2.10", "1.2.9", 1);
Check("1.2", "1.10", -1);
Check("1", "1.0.0", 0);
Check("1.0.1", "1", 1);
Check("2.0", "10.0", -1);
Check("01.2", "1.2", 0);
Check("0.9.9", "1", -1);
Check("3.4.5", "3.4.5.0.0", 0);
Check("1.0.0.1", "1.0.0.0.9", 1);

return failures == 0 ? 0 : 1;
