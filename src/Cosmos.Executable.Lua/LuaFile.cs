// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;

namespace Cosmos.Executable.Lua;

/// <summary>
/// How <c>loadfile</c>, <c>dofile</c> and <c>require</c> reach a script:
/// through <see cref="System.IO"/>, from the state's working directory.
/// Replaces UniLua's loader, which read from Unity's streaming assets.
/// </summary>
internal static class LuaFile
{
    /// <summary>
    /// Opens <paramref name="filename"/> for the lexer: a script as UTF-8 (a
    /// byte order mark is skipped), a precompiled chunk, which starts with
    /// ESC as <c>string.dump</c> writes it, one byte per character.
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

/// <summary>A script file as the lexer reads it, one character at a time.</summary>
internal sealed class FileLoadInfo : ILoadInfo, IDisposable
{
    private readonly StreamReader _reader;

    /// <summary>Characters read ahead of the lexer: the first one, and those <see cref="PeekByte"/> saw.</summary>
    private readonly Queue<char> _buffer = new();

    /// <summary>The first byte of a precompiled chunk (LUA_SIGNATURE).</summary>
    private const int BinaryChunkMark = 0x1B;

    /// <summary>Whether the file is a precompiled chunk, which is read one byte per character.</summary>
    private readonly bool _binary;

    public FileLoadInfo(Stream stream)
    {
        _binary = IsBinaryChunk(stream);
        _reader = _binary
            ? new StreamReader(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: false)
            : new StreamReader(stream, Encoding.UTF8);
    }

    /// <summary>
    /// Whether the file holds a precompiled chunk, after a first line that
    /// starts with <c>#</c> if there is one, as luaL_loadfilex looks; the
    /// stream is left at its start.
    /// </summary>
    private static bool IsBinaryChunk(Stream stream)
    {
        int c = stream.ReadByte();
        if (c == '#')
        {
            while (c != -1 && c != '\n')
            {
                c = stream.ReadByte();
            }

            c = stream.ReadByte();
        }

        stream.Seek(0, SeekOrigin.Begin);
        return c == BinaryChunkMark;
    }

    public int ReadByte()
    {
        return _buffer.Count > 0 ? _buffer.Dequeue() : _reader.Read();
    }

    public int PeekByte()
    {
        if (_buffer.Count > 0)
        {
            return _buffer.Peek();
        }

        int c = _reader.Read();
        if (c != -1)
        {
            _buffer.Enqueue((char)c);
        }

        return c;
    }

    /// <summary>Skips a first line that starts with <c>#</c>, as in a script run as <c>#!/usr/bin/lua</c>.</summary>
    public void SkipComment()
    {
        int c = _reader.Read();
        if (c == '#')
        {
            do
            {
                c = _reader.Read();
            }
            while (c != -1 && c != '\n');

            if (!_binary)
            {
                _buffer.Enqueue('\n'); // keep the line numbers; a chunk must start with its signature
            }
        }
        else if (c != -1)
        {
            _buffer.Enqueue((char)c);
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}
