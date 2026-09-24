namespace Nmf.Sim.Core;

public static class IntMath
{
    private const long MaxIsqrtInput = 1L << 62;

    /// <summary>Floor of the square root; exact for 0..2^62.</summary>
    public static long Isqrt(long n)
    {
        if (n < 0 || n > MaxIsqrtInput)
            throw new ArgumentOutOfRangeException(nameof(n), n, "Input must be in 0..2^62.");
        if (n < 2)
            return n;
        // Math.Sqrt is correctly rounded (IEEE 754) and the loops fix any error, so the result is exact and platform independent.
        long x = (long)Math.Sqrt(n);
        while (x * x > n) x--;
        while ((x + 1) * (x + 1) <= n) x++;
        return x;
    }

    /// <summary>Integer division rounding toward negative infinity.</summary>
    public static int FloorDiv(int a, int b)
    {
        if (b <= 0)
            throw new ArgumentOutOfRangeException(nameof(b), b, "Divisor must be positive.");
        int q = a / b;
        if (a % b != 0 && a < 0)
            q--;
        return q;
    }
}
