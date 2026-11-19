// A correct answer for code-intervals. It shows that the tests can pass.

public static class Solution
{
    public static int[][] MergeIntervals(int[][] intervals)
    {
        var merged = new List<int[]>();
        foreach (int[] interval in intervals.OrderBy(i => i[0]))
        {
            if (merged.Count > 0 && interval[0] <= merged[^1][1])
            {
                merged[^1][1] = Math.Max(merged[^1][1], interval[1]);
            }
            else
            {
                merged.Add(new[] { interval[0], interval[1] });
            }
        }
        return merged.ToArray();
    }
}
