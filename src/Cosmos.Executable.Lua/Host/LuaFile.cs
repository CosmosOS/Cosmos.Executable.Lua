// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace Cosmos.Executable.Lua;

/// <summary>
/// How <c>loadfile</c>, <c>dofile</c> and <c>require</c> reach a script:
/// through <see cref="System.IO"/>, from the state's working directory.
/// Replaces UniLua's loader, which read from Unity's streaming assets.
/// </summary>
internal static class LuaFile
{
    /// <summary>
    /// Opens the file the Lua string <paramref name="filename"/> names, for
    /// the lexer, which reads its bytes.
    /// </summary>
    public static FileLoadInfo OpenFile(ILuaState lua, string filename)
    {
        string path = LuaHost.Of(lua).ResolvePath(filename);
        return new FileLoadInfo(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
    }

    /// <summary>True when <paramref name="filename"/> names a file that can be opened for reading.</summary>
    public static bool Readable(ILuaState lua, string filename)
    {
        try
        {
            using FileStream stream = new(LuaHost.Of(lua).ResolvePath(filename), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch (Exception e) when (IsFileError(e))
        {
            return false;
        }
    }

    /// <summary>
    /// True for what opening, reading or writing a file throws when the file
    /// is missing, locked or not allowed, or its name is not valid: a failure
    /// the library reports to the script, not a bug.
    /// </summary>
    public static bool IsFileError(Exception e)
    {
        return e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException;
    }
}

/// <summary>A script file as the lexer reads it, one byte at a time.</summary>
internal sealed class FileLoadInfo : ILoadInfo, IDisposable
{
    private readonly Stream _stream;

    /// <summary>Bytes read ahead of the lexer: those <see cref="SkipComment"/> kept, and those <see cref="PeekByte"/> saw.</summary>
    private readonly Queue<int> _buffer = new();

    public FileLoadInfo(Stream stream)
    {
        _stream = new BufferedStream(stream);
    }

    public int ReadByte()
    {
        return _buffer.Count > 0 ? _buffer.Dequeue() : _stream.ReadByte();
    }

    public int PeekByte()
    {
        if (_buffer.Count > 0)
        {
            return _buffer.Peek();
        }

        int c = _stream.ReadByte();
        if (c != -1)
        {
            _buffer.Enqueue(c);
        }

        return c;
    }

    /// <summary>
    /// skipcomment, as luaL_loadfilex does: skips a UTF-8 byte order mark,
    /// and a first line that starts with <c>#</c>, as in a script run as
    /// <c>#!/usr/bin/lua</c>, which stays as a newline to keep the line
    /// numbers of a script, but not of a precompiled chunk.
    /// </summary>
    public void SkipComment()
    {
        List<int> prefix = [];
        int c = SkipBom(prefix);
        foreach (int b in prefix)
        {
            _buffer.Enqueue(b); // what was read of a mark that is not one
        }

        if (c == '#') // first line is a comment (Unix exec. file)?
        {
            do
            {
                c = _stream.ReadByte();
            }
            while (c != -1 && c != '\n');

            c = _stream.ReadByte(); // skip end-of-line, if present
            if (c != LuaConf.LUA_SIGNATURE[0])
            {
                _buffer.Enqueue('\n'); // add line to correct line numbers
            }
        }

        if (c != -1)
        {
            _buffer.Enqueue(c); // the first character of the stream
        }
    }

    /// <summary>skipBOM: the first byte after a UTF-8 byte order mark; <paramref name="prefix"/> gets what was read of one that is not.</summary>
    private int SkipBom(List<int> prefix)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        for (int n = 0; n < bom.Length; n++)
        {
            int c = _stream.ReadByte();
            if (c == -1 || c != bom[n])
            {
                return c;
            }

            prefix.Add(c);
        }

        prefix.Clear(); // prefix matched; discard it
        return _stream.ReadByte(); // return next character
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
