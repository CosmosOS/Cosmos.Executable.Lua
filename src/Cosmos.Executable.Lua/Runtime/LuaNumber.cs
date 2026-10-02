// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.Globalization;

namespace Cosmos.Executable.Lua;

/// <summary>
/// Numbers as Lua 5.4 writes them: an integer in decimal, and a float as
/// <c>%.14g</c>, with <c>.0</c> added when that looks like an integer:
/// <c>tostring(1/3)</c> is <c>0.33333333333333</c>, <c>tostring(2^53)</c>
/// is <c>9.007199254741e+15</c> and <c>tostring(3.0)</c> is <c>3.0</c>, on
/// any culture.
/// </summary>
/// <remarks>
/// UniLua wrote them with <see cref="double.ToString()"/>, which writes
/// <c>0.3333333333333333</c>, <c>9.007199254740992E+15</c>, and in a culture
/// with a decimal comma, <c>0,5</c>.
/// </remarks>
internal static class LuaNumber
{
    /// <summary>The significant digits of <c>%.14g</c>.</summary>
    private const int Precision = 14;

    /// <summary>The string <c>tostring</c> makes of an integer.</summary>
    public static string ToString(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The string <c>tostring</c> makes of a float.</summary>
    public static string ToString(double value)
    {
        string text = Format(value, Precision, alternate: false);

        // Looks like an integer? Then it says it is a float
        foreach (char c in text)
        {
            if (c != '-' && (c < '0' || c > '9'))
            {
                return text;
            }
        }

        return text + ".0";
    }

    /// <summary>Formats <paramref name="value"/> as C's <c>%.{precision}g</c>, or <c>%#.{precision}g</c>.</summary>
    public static string Format(double value, int precision, bool alternate)
    {
        if (double.IsNaN(value))
        {
            return double.IsNegative(value) ? "-nan" : "nan";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "inf" : "-inf";
        }

        if (precision == 0)
        {
            precision = 1;
        }

        // The exponent %e would write decides between %e and %f, after rounding
        string scientific = value.ToString("E" + (precision - 1), CultureInfo.InvariantCulture);
        int exponent = int.Parse(scientific.AsSpan(scientific.IndexOf('E') + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        string result;
        if (exponent < -4 || exponent >= precision)
        {
            int mark = scientific.IndexOf('E');
            string mantissa = alternate ? scientific[..mark] : TrimZeros(scientific[..mark]);
            result = mantissa + FormatExponent(exponent);
        }
        else
        {
            string fixedPoint = value.ToString("F" + (precision - 1 - exponent), CultureInfo.InvariantCulture);
            result = alternate ? fixedPoint : TrimZeros(fixedPoint);
        }

        if (alternate && result.IndexOf('.') < 0)
        {
            int mark = result.IndexOf('e');
            result = mark < 0 ? result + "." : result[..mark] + "." + result[mark..];
        }

        // -0 keeps its sign, as printf does
        return value == 0 && double.IsNegative(value) && result[0] != '-' ? "-" + result : result;
    }

    /// <summary>The exponent as C writes it: a sign, and at least two digits.</summary>
    public static string FormatExponent(int exponent)
    {
        return (exponent < 0 ? "e-" : "e+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
    }

    private static string TrimZeros(string digits)
    {
        if (digits.IndexOf('.') < 0)
        {
            return digits;
        }

        digits = digits.TrimEnd('0');
        return digits.EndsWith('.') ? digits[..^1] : digits;
    }
}
