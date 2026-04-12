using System.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

internal static class Math
{
    [RuntimeExport("ceil")]
    internal static double ceil(double x)
    {
        // If the value is already an integer, return it directly
        if (x == (long)x)
        {
            return x;
        }

        // Handle special floating-point values
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (double.IsPositiveInfinity(x))
        {
            return double.PositiveInfinity;
        }

        if (double.IsNegativeInfinity(x))
        {
            return double.NegativeInfinity;
        }

        // For positive numbers, truncate and add 1
        if (x > 0)
        {
            return (long)x + 1;
        }

        // For negative numbers, truncation already acts like ceiling
        return (long)x;
    }

    [RuntimeExport("ceilf")]
    internal static float ceilf(float x)
    {
        return (float)ceil(x);
    }

    [RuntimeExport("sqrt")]
    internal static double sqrt(double x)
    {
        // Handle special cases
        if (double.IsNaN(x) || x < 0)
        {
            return double.NaN;
        }

        if (double.IsPositiveInfinity(x))
        {
            return double.PositiveInfinity;
        }

        if (x == 0)
        {
            return 0;
        }

        // Newton-Raphson method for square root
        double guess = x;
        double epsilon = 1e-10;

        for (int i = 0; i < 50; i++)
        {
            double nextGuess = (guess + x / guess) / 2.0;
            if (System.Math.Abs(nextGuess - guess) < epsilon)
            {
                break;
            }

            guess = nextGuess;
        }

        return guess;
    }


    [RuntimeExport("trunc")]
    internal static double trunc(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (double.IsInfinity(x))
        {
            return x;
        }

        return (long)x;
    }

    [RuntimeExport("truncf")]
    internal static float truncf(float x)
    {
        return (float)trunc(x);
    }

    [RuntimeExport("modf")]
    internal static unsafe double ModF(double x, double* intptr)
    {
        if (double.IsNaN(x))
        {
            *intptr = double.NaN;
            return double.NaN;
        }

        if (double.IsInfinity(x))
        {
            *intptr = x;
            return 0.0;
        }

        long intPart = (long)x;
        *intptr = (double)intPart;
        return x - intPart;
    }

    [RuntimeExport("fma")]
    internal static double fma(double x, double y, double z)
    {
        return x * y + z;
    }

    [RuntimeExport("fmaf")]
    internal static float fmaf(float x, float y, float z)
    {
        return x * y + z;
    }
}
