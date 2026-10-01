// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.Text;

namespace Cosmos.Executable.Lua;

/// <summary>
/// Lua strings hold bytes, as in C Lua: here a .NET string whose characters
/// are <c>\0</c> to <c>\xFF</c>, one per byte. Text goes into Lua as its
/// UTF-8 bytes, and comes back decoded from UTF-8, which is what this class
/// does: <c>#"é"</c> is 2, and the <c>utf8</c> library reads the bytes.
/// </summary>
/// <remarks>
/// <see cref="LuaInterpreter"/> converts what it takes and gives, and the
/// libraries convert the console's text and file names; a C# function on
/// the <see cref="ILuaState"/> API gets and pushes Lua strings as they are,
/// and converts text with <see cref="Encode"/> and <see cref="Decode"/>.
/// </remarks>
public static class LuaText
{
    /// <summary>The Lua string of <paramref name="text"/>: its UTF-8 bytes.</summary>
    public static string Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Ascii.IsValid(text) ? text : FromBytes(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// The text of the Lua string <paramref name="luaString"/>, decoded from
    /// UTF-8; a byte that is not part of a UTF-8 sequence becomes U+FFFD.
    /// </summary>
    public static string Decode(string luaString)
    {
        ArgumentNullException.ThrowIfNull(luaString);
        return Ascii.IsValid(luaString) ? luaString : Encoding.UTF8.GetString(ToBytes(luaString));
    }

    /// <summary>The Lua string of <paramref name="bytes"/>, one character per byte.</summary>
    internal static string FromBytes(ReadOnlySpan<byte> bytes)
    {
        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// The bytes of the Lua string <paramref name="luaString"/>; a character
    /// above <c>\xFF</c>, which only a C# function can push, as its UTF-8 bytes.
    /// </summary>
    internal static byte[] ToBytes(string luaString)
    {
        foreach (char c in luaString)
        {
            if (c > 0xFF)
            {
                return ToBytesSlow(luaString);
            }
        }

        return Encoding.Latin1.GetBytes(luaString);
    }

    private static byte[] ToBytesSlow(string luaString)
    {
        var bytes = new System.Collections.Generic.List<byte>(luaString.Length + 8);
        Span<byte> utf8 = stackalloc byte[4];
        for (int i = 0; i < luaString.Length; i++)
        {
            char c = luaString[i];
            if (c <= 0xFF)
            {
                bytes.Add((byte)c);
                continue;
            }

            int length = char.IsHighSurrogate(c) && i + 1 < luaString.Length && char.IsLowSurrogate(luaString[i + 1])
                ? Encoding.UTF8.GetBytes(luaString.AsSpan(i++, 2), utf8)
                : Encoding.UTF8.GetBytes(luaString.AsSpan(i, 1), utf8);
            for (int j = 0; j < length; j++)
            {
                bytes.Add(utf8[j]);
            }
        }

        return bytes.ToArray();
    }
}
