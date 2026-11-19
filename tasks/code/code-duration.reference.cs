// A correct answer for code-duration. It shows that the tests can pass.

public static class Solution
{
    public static int ParseDuration(string s)
    {
        if (s.Length == 0)
        {
            return -1;
        }

        int[] secondsPerUnit = { 3600, 60, 1 };
        int total = 0;
        int nextUnit = 0; // Units must come in the order h, m, s, so each part must use a later unit than the last.
        int i = 0;
        while (i < s.Length)
        {
            int start = i;
            int number = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                number = number * 10 + (s[i] - '0');
                i++;
            }
            if (i == start || i == s.Length)
            {
                return -1;
            }

            int unit = "hms".IndexOf(s[i]);
            if (unit < nextUnit)
            {
                return -1;
            }
            total += number * secondsPerUnit[unit];
            nextUnit = unit + 1;
            i++;
        }
        return total;
    }
}
