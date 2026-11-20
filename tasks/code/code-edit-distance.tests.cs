// Tests for code-edit-distance. They're built together with the reply's Solution class. Exit code 0 means every check passed.

int failures = 0;

void Check(string a, string b, int expected)
{
    int actual = Solution.Distance(a, b);
    if (actual != expected)
    {
        Console.WriteLine($"Distance(\"{a}\", \"{b}\"): expected {expected}, got {actual}");
        failures++;
    }
}

Check("", "", 0);
Check("", "abc", 3);
Check("abc", "", 3);
Check("abc", "abc", 0);
Check("a", "A", 1);
Check("kitten", "sitting", 3);
Check("flaw", "lawn", 2);
Check("abc", "cba", 2);
Check("Saturday", "Sunday", 3);
Check("intention", "execution", 5);

return failures == 0 ? 0 : 1;
