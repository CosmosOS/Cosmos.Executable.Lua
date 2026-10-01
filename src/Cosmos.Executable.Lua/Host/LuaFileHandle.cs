// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.IO;
using System.Text;

namespace Cosmos.Executable.Lua;

/// <summary>
/// A file of the <c>io</c> library: a <see cref="Stream"/>, or one of the
/// standard files, which are the host's console.
/// </summary>
/// <remarks>
/// Lua strings are .NET strings here, as everywhere in UniLua. A file opened
/// in text mode is UTF-8, as scripts are, so text written by a script reads
/// back the same; in binary mode (<c>"rb"</c>, <c>"wb"</c>...) every byte is
/// one character, from <c>\0</c> to <c>\255</c>, so bytes make the round
/// trip whatever they are.
/// </remarks>
internal sealed class LuaFileHandle
{
    private readonly Stream? _stream;
    private readonly LuaHost _host;
    private readonly StandardFile _standard;
    private readonly bool _binary;
    private readonly bool _append;

    /// <summary>The byte <see cref="UnreadByte"/> put back, or -1.</summary>
    private int _pushback = -1;

    /// <summary>The second half of a surrogate pair <see cref="ReadChar"/> decoded, or -1.</summary>
    private int _lowSurrogate = -1;

    /// <summary>What is left of the line standard input last gave.</summary>
    private string? _consoleLine;
    private int _consolePosition;

    /// <summary>A file on a stream, which <paramref name="host"/> closes with the others it left open.</summary>
    public LuaFileHandle(LuaHost host, Stream stream, bool binary, bool append)
    {
        _host = host;
        _stream = stream;
        _binary = binary;
        _append = append;
        host.OpenFiles.Add(this);
    }

    /// <summary>One of the standard files, on the console of <paramref name="host"/>.</summary>
    public LuaFileHandle(LuaHost host, StandardFile standard)
    {
        _host = host;
        _standard = standard;
    }

    /// <summary>The standard files, which <c>io.close</c> leaves open.</summary>
    public enum StandardFile
    {
        None,
        Input,
        Output,
        Error,
    }

    public bool IsClosed { get; private set; }

    public bool IsStandard => _standard != StandardFile.None;

    /// <summary>Reads one character; -1 at the end of the file.</summary>
    /// <remarks>A character outside the BMP comes as its two surrogates, in two calls.</remarks>
    public int ReadChar()
    {
        if (_standard == StandardFile.Input)
        {
            return ReadConsoleChar();
        }

        if (_lowSurrogate >= 0)
        {
            int low = _lowSurrogate;
            _lowSurrogate = -1;
            return low;
        }

        int b = ReadByte();
        if (b < 0x80 || _binary)
        {
            return b;
        }

        // A UTF-8 sequence: its lead byte says how many bytes follow
        int length = b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 1;
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)b;
        int count = 1;
        while (count < length)
        {
            int next = ReadByte();
            if (next is < 0x80 or >= 0xC0)
            {
                // not a continuation byte: it starts the next character
                UnreadByte(next);
                break;
            }

            bytes[count++] = (byte)next;
        }

        Span<char> chars = stackalloc char[2];
        int decoded = Encoding.UTF8.GetChars(bytes[..count], chars);
        if (decoded == 2)
        {
            _lowSurrogate = chars[1];
        }

        return chars[0];
    }

    /// <summary>Reads the next byte without consuming it; -1 at the end. Characters of standard input are bytes here.</summary>
    public int PeekByte()
    {
        if (_standard == StandardFile.Input)
        {
            int c = ReadConsoleChar();
            if (c >= 0)
            {
                _consolePosition--;
            }

            return c;
        }

        int b = ReadByte();
        UnreadByte(b);
        return b;
    }

    /// <summary>Consumes the byte <see cref="PeekByte"/> returned.</summary>
    public void SkipByte()
    {
        if (_standard == StandardFile.Input)
        {
            ReadConsoleChar();
            return;
        }

        ReadByte();
    }

    public void Write(string text)
    {
        switch (_standard)
        {
            case StandardFile.Output:
                _host.Out.Write(text);
                return;
            case StandardFile.Error:
                _host.Err.Write(text);
                return;
            case StandardFile.Input:
                throw new IOException("Bad file descriptor");
        }

        if (_append)
        {
            _stream!.Seek(0, SeekOrigin.End);
        }

        byte[] bytes;
        if (_binary)
        {
            bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                bytes[i] = text[i] <= 0xFF ? (byte)text[i] : (byte)'?';
            }
        }
        else
        {
            bytes = Encoding.UTF8.GetBytes(text);
        }

        DropReadAhead();
        _stream!.Write(bytes, 0, bytes.Length);

        // Through to the file system at once: no collector flushes a file a
        // script forgot to close
        _stream.Flush();
    }

    public void Flush()
    {
        switch (_standard)
        {
            case StandardFile.Output:
                _host.Out.Flush();
                return;
            case StandardFile.Error:
                _host.Err.Flush();
                return;
            case StandardFile.Input:
                return;
        }

        _stream!.Flush();
    }

    /// <summary>Moves to <paramref name="offset"/> bytes from <paramref name="origin"/>, and returns the new position.</summary>
    public long Seek(SeekOrigin origin, long offset)
    {
        if (_stream is null)
        {
            throw new IOException("Illegal seek");
        }

        if (origin == SeekOrigin.Current && _pushback >= 0)
        {
            offset--; // the stream is one byte ahead of the script
        }

        DropReadAhead();
        return _stream.Seek(offset, origin);
    }

    public void Close()
    {
        IsClosed = true;
        if (_stream is not null)
        {
            _host.OpenFiles.Remove(this);
            _stream.Dispose();
        }
    }

    private int ReadByte()
    {
        if (_pushback >= 0)
        {
            int b = _pushback;
            _pushback = -1;
            return b;
        }

        return _stream!.ReadByte();
    }

    private void UnreadByte(int b)
    {
        _pushback = b;
    }

    /// <summary>Forgets what was read ahead of the script, before the stream moves.</summary>
    private void DropReadAhead()
    {
        _pushback = -1;
        _lowSurrogate = -1;
    }

    private int ReadConsoleChar()
    {
        while (_consoleLine is null || _consolePosition >= _consoleLine.Length)
        {
            string? line = _host.In.ReadLine();
            if (line is null)
            {
                return -1;
            }

            _consoleLine = line + "\n";
            _consolePosition = 0;
        }

        return _consoleLine[_consolePosition++];
    }
}
