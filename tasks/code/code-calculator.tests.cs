// Tests for code-calculator. They're built together with the reply's Solution class. Exit code 0 means every check passed.

int failures = 0;

void Check(string input, long expected)
{
    long actual = Solution.Evaluate(input);
    if (actual != expected)
    {
        Console.WriteLine($"Evaluate(\"{input}\"): expected {expected}, got {actual}");
        failures++;
    }
}

Check("1+2", 3);
Check("2+3*4", 14);
Check("(2+3)*4", 20);
Check("10-4-3", 3);
Check("100/10/5", 2);
Check(" 7 / 2 ", 3);
Check("-7/2", -3);
Check("-(2+3)*2", -10);
Check("2*-3", -6);
Check("2-(-3)", 5);
Check("-(-(4))", 4);
Check("((1))", 1);
Check("3*(4+(5-2)*2)-6/4", 29);
Check("1000000*1000000", 1000000000000);

return failures == 0 ? 0 : 1;
