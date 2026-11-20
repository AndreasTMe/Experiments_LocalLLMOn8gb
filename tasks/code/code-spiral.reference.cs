// A correct answer for code-spiral. It shows that the tests can pass.

public static class Solution
{
    public static int[] SpiralOrder(int[,] matrix)
    {
        int top = 0;
        int bottom = matrix.GetLength(0) - 1;
        int left = 0;
        int right = matrix.GetLength(1) - 1;
        var result = new List<int>();
        while (top <= bottom && left <= right)
        {
            for (int c = left; c <= right; c++)
            {
                result.Add(matrix[top, c]);
            }
            top++;
            for (int r = top; r <= bottom; r++)
            {
                result.Add(matrix[r, right]);
            }
            right--;
            if (top <= bottom)
            {
                for (int c = right; c >= left; c--)
                {
                    result.Add(matrix[bottom, c]);
                }
                bottom--;
            }
            if (left <= right)
            {
                for (int r = bottom; r >= top; r--)
                {
                    result.Add(matrix[r, left]);
                }
                left++;
            }
        }
        return result.ToArray();
    }
}
