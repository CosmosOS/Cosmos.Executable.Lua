// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.IO;

namespace Cosmos.Executable.Lua;

/// <summary>
/// A file of the <c>io</c> library: a <see cref="Stream"/>, or one of the
/// standard files, which are the host's console.
/// </summary>
/// <remarks>
/// Lua strings hold bytes (see <see cref="LuaText"/>): a file gives and
/// takes its bytes as they are, in text mode as in binary mode, as on
/// POSIX. The console is text, which the standard files encode to and
/// decode from UTF-8. A write reaches the file at once, unless the script
/// asked <c>setvbuf</c> for a buffer: no collector flushes the buffer of a
/// file a script forgot to close (see <see cref="BufferMode"/>).
/// </remarks>
internal sealed class LuaFileHandle
{
    private readonly Stream? _stream;
    private readonly LuaHost _host;
    private readonly StandardFile _standard;
    private readonly bool _append;

    /// <summary>The size of a buffer <c>setvbuf</c> gives no size, LUAL_BUFFERSIZE.</summary>
    public const int DefaultBufferSize = 8192;

    /// <summary>The bytes written that have not reached the stream yet.</summary>
    private byte[]? _writeBuffer;
    private int _writeCount;
    private int _bufferSize = DefaultBufferSize;
    private BufferMode _bufferMode = BufferMode.No;

    /// <summary>The byte <see cref="UnreadByte"/> put back, or -1.</summary>
    private int _pushback = -1;

    /// <summary>What is left of the line standard input last gave, as bytes.</summary>
    private string? _consoleLine;
    private int _consolePosition;

    /// <summary>A file on a stream, which <paramref name="host"/> closes with the others it left open.</summary>
    public LuaFileHandle(LuaHost host, Stream stream, bool append)
    {
        _host = host;
        _stream = stream;
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

    /// <summary>When the writes to a file reach its stream, as the modes of <c>setvbuf</c>.</summary>
    public enum BufferMode
    {
        /// <summary>At once.</summary>
        No,

        /// <summary>When the buffer is full.</summary>
        Full,

        /// <summary>At the end of a line, or when the buffer is full.</summary>
        Line,
    }

    public bool IsClosed { get; private set; }

    public bool IsStandard => _standard != StandardFile.None;

    /// <summary>Reads one byte; -1 at the end of the file.</summary>
    public int ReadChar()
    {
        return _standard == StandardFile.Input ? ReadConsoleChar() : ReadByte();
    }

    /// <summary>Reads the next byte without consuming it; -1 at the end.</summary>
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

    /// <summary>Puts back the byte <see cref="ReadChar"/> read last, as ungetc.</summary>
    public void Unread(int b)
    {
        if (_standard == StandardFile.Input)
        {
            if (_consolePosition > 0)
            {
                _consolePosition--;
            }

            return;
        }

        UnreadByte(b);
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

    /// <summary>Writes the bytes of the Lua string <paramref name="text"/>.</summary>
    public void Write(string text)
    {
        switch (_standard)
        {
            case StandardFile.Output:
                _host.WriteOut(text);
                return;
            case StandardFile.Error:
                _host.WriteErr(text);
                return;
            case StandardFile.Input:
                throw new NotSupportedException(); // EBADF: standard input is not open for writing
        }

        byte[] bytes = LuaText.ToBytes(text);
        DropReadAhead();
        if (_bufferMode == BufferMode.No || bytes.Length >= _bufferSize)
        {
            FlushBuffer();
            WriteThrough(bytes, bytes.Length);
            return;
        }

        if (_writeCount + bytes.Length > _bufferSize)
        {
            FlushBuffer();
        }

        _writeBuffer ??= new byte[_bufferSize];
        Array.Copy(bytes, 0, _writeBuffer, _writeCount, bytes.Length);
        _writeCount += bytes.Length;
        if (_bufferMode == BufferMode.Line && Array.IndexOf(bytes, (byte)'\n') >= 0)
        {
            FlushBuffer();
        }
    }

    /// <summary>Sets how the writes reach the file, as <c>setvbuf</c>; the console is not buffered.</summary>
    public void SetBuffering(BufferMode mode, int size)
    {
        if (_stream is null)
        {
            return;
        }

        FlushBuffer();
        _bufferMode = mode;
        _bufferSize = Math.Max(1, size);
        _writeBuffer = null;
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

        FlushBuffer();
        _stream!.Flush();
    }

    /// <summary>Moves to <paramref name="offset"/> bytes from <paramref name="origin"/>, and returns the new position.</summary>
    public long Seek(SeekOrigin origin, long offset)
    {
        if (_stream is null)
        {
            throw new IOException("Illegal seek");
        }

        FlushBuffer();
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
            try
            {
                FlushBuffer();
            }
            finally
            {
                _host.OpenFiles.Remove(this);
                _stream.Dispose();
            }
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

        if (_stream is null)
        {
            throw new NotSupportedException(); // EBADF: standard output and error are not open for reading
        }

        FlushBuffer(); // what was written is there to read
        return _stream.ReadByte();
    }

    /// <summary>Writes the buffered bytes through to the file.</summary>
    private void FlushBuffer()
    {
        if (_writeCount == 0)
        {
            return;
        }

        int count = _writeCount;
        _writeCount = 0; // forgotten even when the disk fails, as fflush
        WriteThrough(_writeBuffer!, count);
    }

    private void WriteThrough(byte[] bytes, int count)
    {
        if (_append)
        {
            _stream!.Seek(0, SeekOrigin.End);
        }

        _stream!.Write(bytes, 0, count);
        _stream.Flush();
    }

    private void UnreadByte(int b)
    {
        _pushback = b;
    }

    /// <summary>Forgets what was read ahead of the script, before the stream moves.</summary>
    private void DropReadAhead()
    {
        _pushback = -1;
    }

    private int ReadConsoleChar()
    {
        while (_consoleLine is null || _consolePosition >= _consoleLine.Length)
        {
            string? line = _host.ReadInLine();
            if (line is null)
            {
                return -1;
            }

            _consoleLine = line;
            _consolePosition = 0;
        }

        return _consoleLine[_consolePosition++];
    }
}
