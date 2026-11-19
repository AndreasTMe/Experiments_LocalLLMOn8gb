// A correct answer for code-roman. It shows that the tests can pass.

public static class Solution
{
    private static readonly int[] Values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
    private static readonly string[] Symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

    public static string ToRoman(int n)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < Values.Length; i++)
        {
            while (n >= Values[i])
            {
                result.Append(Symbols[i]);
                n -= Values[i];
            }
        }
        return result.ToString();
    }
}
