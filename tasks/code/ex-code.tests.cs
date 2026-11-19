// Tests for ex-code. They're built together with the reply's Solution class. Exit code 0 means every check passed.

int failures = 0;

void Check(string input, string expected)
{
    string actual = Solution.Compress(input);
    if (actual != expected)
    {
        Console.WriteLine($"Compress(\"{input}\"): expected \"{expected}\", got \"{actual}\"");
        failures++;
    }
}

Check("", "");
Check("a", "a1");
Check("aaabcc", "a3b1c2");
Check("abc", "a1b1c1");
Check("aaaaaaaaaaaa", "a12");
Check("aaAA", "a2A2");
Check("  !!", " 2!2");
Check("abba", "a1b2a1");

return failures == 0 ? 0 : 1;
