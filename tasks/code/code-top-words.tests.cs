// Tests for code-top-words. They're built together with the reply's Solution class. Exit code 0 means every check passed.

int failures = 0;

void Check(string text, int k, string expected)
{
    string actual = string.Join(",", Solution.TopWords(text, k));
    if (actual != expected)
    {
        Console.WriteLine($"TopWords(\"{text}\", {k}): expected [{expected}], got [{actual}]");
        failures++;
    }
}

Check("", 3, "");
Check("hello", 0, "");
Check("The cat and the hat", 2, "the,and");
Check("Dog dog DOG cat", 5, "dog,cat");
Check("b a c b a", 2, "a,b");
Check("it's a dog's life", 3, "s,a,dog");
Check("x1y2z3 x", 10, "x,y,z");
Check("café latte", 5, "caf,latte");
Check("one, two; two! three? three... three", 2, "three,two");

return failures == 0 ? 0 : 1;
