// A correct answer for code-versions. It shows that the tests can pass.

public static class Solution
{
    public static int CompareVersions(string a, string b)
    {
        string[] partsA = a.Split('.');
        string[] partsB = b.Split('.');
        for (int i = 0; i < Math.Max(partsA.Length, partsB.Length); i++)
        {
            int x = i < partsA.Length ? int.Parse(partsA[i]) : 0;
            int y = i < partsB.Length ? int.Parse(partsB[i]) : 0;
            if (x != y)
            {
                return x < y ? -1 : 1;
            }
        }
        return 0;
    }
}
