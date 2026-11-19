// Tests for code-roman. They're built together with the reply's Solution class. Exit code 0 means every check passed.

int failures = 0;

void Check(int input, string expected)
{
    string actual = Solution.ToRoman(input);
    if (actual != expected)
    {
        Console.WriteLine($"ToRoman({input}): expected \"{expected}\", got \"{actual}\"");
        failures++;
    }
}

Check(1, "I");
Check(3, "III");
Check(4, "IV");
Check(9, "IX");
Check(14, "XIV");
Check(40, "XL");
Check(90, "XC");
Check(400, "CD");
Check(944, "CMXLIV");
Check(1994, "MCMXCIV");
Check(2026, "MMXXVI");
Check(3999, "MMMCMXCIX");

return failures == 0 ? 0 : 1;
