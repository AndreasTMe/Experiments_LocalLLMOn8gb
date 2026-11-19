// Tests for code-intervals. They're built together with the reply's Solution class. Exit code 0 means every check passed.

int failures = 0;

string Format(int[][] intervals)
{
    return string.Join(",", intervals.Select(i => $"[{string.Join(",", i)}]"));
}

void Check(int[][] input, string expected)
{
    string before = Format(input);
    string actual = Format(Solution.MergeIntervals(input));
    if (actual != expected)
    {
        Console.WriteLine($"MergeIntervals({before}): expected {expected}, got {actual}");
        failures++;
    }
}

Check([], "");
Check([[1, 3]], "[1,3]");
Check([[1, 3], [2, 6], [8, 10], [15, 18]], "[1,6],[8,10],[15,18]");
Check([[8, 10], [1, 3], [2, 6]], "[1,6],[8,10]");
Check([[1, 4], [4, 5]], "[1,5]");
Check([[1, 2], [3, 4]], "[1,2],[3,4]");
Check([[5, 6], [1, 2], [2, 3]], "[1,3],[5,6]");
Check([[1, 10], [2, 3], [4, 5]], "[1,10]");
Check([[3, 3], [3, 3]], "[3,3]");
Check([[-5, -1], [-2, 0]], "[-5,0]");

return failures == 0 ? 0 : 1;
