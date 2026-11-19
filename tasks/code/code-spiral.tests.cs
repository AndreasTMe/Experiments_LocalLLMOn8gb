// Tests for code-spiral. They're built together with the reply's Solution class. Exit code 0 means every check passed.

int failures = 0;

void Check(string name, int[,] matrix, string expected)
{
    string actual = string.Join(",", Solution.SpiralOrder(matrix));
    if (actual != expected)
    {
        Console.WriteLine($"SpiralOrder({name}): expected [{expected}], got [{actual}]");
        failures++;
    }
}

Check("0x0", new int[0, 0], "");
Check("1x1", new int[,] { { 1 } }, "1");
Check("3x3", new int[,] { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } }, "1,2,3,6,9,8,7,4,5");
Check("3x4", new int[,] { { 1, 2, 3, 4 }, { 5, 6, 7, 8 }, { 9, 10, 11, 12 } }, "1,2,3,4,8,12,11,10,9,5,6,7");
Check("2x3", new int[,] { { 1, 2, 3 }, { 4, 5, 6 } }, "1,2,3,6,5,4");
Check("4x2", new int[,] { { 1, 2 }, { 3, 4 }, { 5, 6 }, { 7, 8 } }, "1,2,4,6,8,7,5,3");
Check("1x4", new int[,] { { 1, 2, 3, 4 } }, "1,2,3,4");
Check("4x1", new int[,] { { 1 }, { 2 }, { 3 }, { 4 } }, "1,2,3,4");

return failures == 0 ? 0 : 1;
