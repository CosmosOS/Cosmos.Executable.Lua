// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cosmos.Executable.Lua;

/// <summary>
/// The <c>os</c> library of Lua 5.2, where UniLua had <c>os.clock</c> only,
/// on <see cref="Process"/>, which a kernel does not have.
/// </summary>
/// <remarks>
/// On a Cosmos kernel the local time is UTC, there are no environment
/// variables (<c>os.getenv</c> gives nil), and <c>os.execute</c> runs a
/// command only if the host gave the interpreter a way to
/// (<see cref="LuaInterpreter.ExecuteCommand"/>).
/// </remarks>
internal static class LuaOSLib
{
    public const string LIB_NAME = "os";

    /// <summary>How many names <c>os.tmpname</c> tries before it gives up.</summary>
    private const int TmpnameAttempts = 100;

    public static int OpenLib(ILuaState lua)
    {
        NameFuncPair[] library =
        [
            new("clock", OS_Clock),
            new("date", OS_Date),
            new("difftime", OS_Difftime),
            new("execute", OS_Execute),
            new("exit", OS_Exit),
            new("getenv", OS_Getenv),
            new("remove", OS_Remove),
            new("rename", OS_Rename),
            new("setlocale", OS_Setlocale),
            new("time", OS_Time),
            new("tmpname", OS_Tmpname),
        ];
        lua.L_NewLib(library);
        return 1;
    }

    private static int OS_Clock(ILuaState lua)
    {
        // The time the state has run for: a kernel thread has no CPU time of its own to report
        long elapsed = Stopwatch.GetTimestamp() - LuaHost.Of(lua).StartTimestamp;
        lua.PushNumber((double)elapsed / Stopwatch.Frequency);
        return 1;
    }

    private static int OS_Difftime(ILuaState lua)
    {
        lua.PushNumber(lua.L_CheckNumber(1) - lua.L_Opt(lua.L_CheckNumber, 2, 0.0));
        return 1;
    }

    private static int OS_Execute(ILuaState lua)
    {
        Func<string, int>? execute = LuaHost.Of(lua).ExecuteCommand;
        if (lua.IsNoneOrNil(1))
        {
            // Is there a shell?
            lua.PushBoolean(execute is not null);
            return 1;
        }

        string command = lua.L_CheckString(1);
        int status = execute is null ? 127 : execute(command); // 127: what a shell says of a command it cannot find
        if (status == 0)
        {
            lua.PushBoolean(true);
        }
        else
        {
            lua.PushNil();
        }

        lua.PushString("exit");
        lua.PushInteger(status);
        return 3;
    }

    private static int OS_Exit(ILuaState lua)
    {
        int code = lua.Type(1) == LuaType.LUA_TBOOLEAN
            ? (lua.ToBoolean(1) ? 0 : 1)
            : lua.L_OptInt(1, 0);

        // A Lua error that no pcall keeps; see LuaState.D_PropagateExit
        LuaHost.Of(lua).ExitCode = code;
        lua.PushString("exit");
        ((LuaState)lua).D_Throw(ThreadStatus.LUA_ERRRUN);
        return 0;
    }

    private static int OS_Getenv(ILuaState lua)
    {
        string? value = Environment.GetEnvironmentVariable(lua.L_CheckString(1));
        if (value is null)
        {
            lua.PushNil();
        }
        else
        {
            lua.PushString(value);
        }

        return 1;
    }

    private static int OS_Remove(ILuaState lua)
    {
        string name = lua.L_CheckString(1);
        string path = LuaHost.Of(lua).ResolvePath(name);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path); // empty ones only, as remove(3)
            }
            else
            {
                return LuaIOLib.PushResult(lua, new FileNotFoundException(), name);
            }
        }
        catch (Exception e) when (LuaFile.IsFileError(e))
        {
            return LuaIOLib.PushResult(lua, e, name);
        }

        lua.PushBoolean(true);
        return 1;
    }

    private static int OS_Rename(ILuaState lua)
    {
        string oldName = lua.L_CheckString(1);
        string newName = lua.L_CheckString(2);
        LuaHost host = LuaHost.Of(lua);
        string oldPath = host.ResolvePath(oldName);
        string newPath = host.ResolvePath(newName);
        try
        {
            if (File.Exists(oldPath))
            {
                File.Move(oldPath, newPath, overwrite: true); // rename(2) replaces the target
            }
            else if (Directory.Exists(oldPath))
            {
                Directory.Move(oldPath, newPath);
            }
            else
            {
                return LuaIOLib.PushResult(lua, new FileNotFoundException(), oldName);
            }
        }
        catch (Exception e) when (LuaFile.IsFileError(e))
        {
            return LuaIOLib.PushResult(lua, e, oldName);
        }

        lua.PushBoolean(true);
        return 1;
    }

    private static int OS_Setlocale(ILuaState lua)
    {
        // The C locale is the only one
        string? locale = lua.IsNoneOrNil(1) ? null : lua.L_CheckString(1);
        if (locale is null or "" or "C" or "POSIX")
        {
            lua.PushString("C");
        }
        else
        {
            lua.PushNil();
        }

        return 1;
    }

    private static int OS_Tmpname(ILuaState lua)
    {
        // As mkstemp does, and not with Path.GetTempFileName, whose native
        // mkstemps a kernel does not link: the name is taken once the file exists
        string directory = Path.GetTempPath();
        for (int attempt = 0; attempt < TmpnameAttempts; attempt++)
        {
            string name = Path.Combine(directory, "lua_" + Random.Shared.Next().ToString("x8", CultureInfo.InvariantCulture));
            try
            {
                new FileStream(name, FileMode.CreateNew, FileAccess.Write).Dispose();
                lua.PushString(name);
                return 1;
            }
            catch (IOException) when (File.Exists(name))
            {
                // taken: another name
            }
            catch (Exception e) when (LuaFile.IsFileError(e))
            {
                break;
            }
        }

        return lua.L_Error("unable to generate a unique filename");
    }

    // ---- time

    private static int OS_Time(ILuaState lua)
    {
        if (lua.IsNoneOrNil(1))
        {
            lua.PushNumber(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return 1;
        }

        lua.L_CheckType(1, LuaType.LUA_TTABLE);
        lua.SetTop(1); // make sure the table is at the top
        // In the order of the reference implementation, which says which field is missing first
        int second = GetField(lua, "sec", 0);
        int minute = GetField(lua, "min", 0);
        int hour = GetField(lua, "hour", 12);
        int day = GetField(lua, "day", -1);
        int month = GetField(lua, "month", -1);
        int year = GetField(lua, "year", -1);

        // Out of range fields carry over, as mktime does: day 0 is the last of the month before
        DateTime local;
        try
        {
            local = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
                .AddMonths(month - 1)
                .AddDays(day - 1)
                .AddHours(hour)
                .AddMinutes(minute)
                .AddSeconds(second);
        }
        catch (ArgumentOutOfRangeException)
        {
            lua.PushNil(); // the time cannot be represented
            return 1;
        }

        DateTime utc = local - LocalOffset(local);
        lua.PushNumber(new DateTimeOffset(utc.Ticks, TimeSpan.Zero).ToUnixTimeSeconds());
        return 1;
    }

    private static int GetField(ILuaState lua, string key, int fallback)
    {
        lua.GetField(-1, key);
        int value = lua.ToIntegerX(-1, out bool isNumber);
        lua.Pop(1);
        if (isNumber)
        {
            return value;
        }

        if (fallback < 0)
        {
            return lua.L_Error("field '{0}' missing in date table", key);
        }

        return fallback;
    }

    private static int OS_Date(ILuaState lua)
    {
        string format = lua.L_OptString(1, "%c");
        double seconds = lua.IsNoneOrNil(2) ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : lua.L_CheckNumber(2);

        DateTime time;
        TimeSpan offset;
        try
        {
            DateTime utc = DateTimeOffset.FromUnixTimeSeconds((long)Math.Floor(seconds)).UtcDateTime;
            bool universal = format.StartsWith('!');
            if (universal)
            {
                format = format[1..];
            }

            offset = universal ? TimeSpan.Zero : LocalOffset(utc);
            time = utc + offset;
        }
        catch (ArgumentOutOfRangeException)
        {
            lua.PushNil(); // the time cannot be represented
            return 1;
        }

        if (format.StartsWith("*t", StringComparison.Ordinal))
        {
            lua.CreateTable(0, 9);
            SetField(lua, "sec", time.Second);
            SetField(lua, "min", time.Minute);
            SetField(lua, "hour", time.Hour);
            SetField(lua, "day", time.Day);
            SetField(lua, "month", time.Month);
            SetField(lua, "year", time.Year);
            SetField(lua, "wday", (int)time.DayOfWeek + 1);
            SetField(lua, "yday", time.DayOfYear);
            lua.PushBoolean(false);
            lua.SetField(-2, "isdst");
            return 1;
        }

        StringBuilder result = new();
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
            {
                result.Append(format[i]);
                continue;
            }

            if (++i >= format.Length)
            {
                return lua.L_Error("invalid conversion specifier '%'");
            }

            // The E and O modifiers ask for the locale's alternative forms, which
            // the C locale does not have, of the conversions C99 allows them on
            if (format[i] is 'E' or 'O')
            {
                string modified = format[i] == 'E' ? "cCxXyY" : "deHImMSuUVwWy";
                if (i + 1 >= format.Length || modified.IndexOf(format[i + 1]) < 0)
                {
                    return lua.L_Error("invalid conversion specifier '%{0}'", format.Substring(i, Math.Min(2, format.Length - i)));
                }

                i++;
            }

            if (!AppendConversion(result, format[i], time, offset))
            {
                return lua.L_Error("invalid conversion specifier '%{0}'", format[i]);
            }
        }

        lua.PushString(result.ToString());
        return 1;
    }

    private static void SetField(ILuaState lua, string key, int value)
    {
        lua.PushInteger(value);
        lua.SetField(-2, key);
    }

    /// <summary>Appends one strftime conversion, in the C locale; false for one it does not know.</summary>
    private static bool AppendConversion(StringBuilder result, char conversion, DateTime time, TimeSpan offset)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        switch (conversion)
        {
            case 'a': result.Append(time.ToString("ddd", c)); break;
            case 'A': result.Append(time.ToString("dddd", c)); break;
            case 'b' or 'h': result.Append(time.ToString("MMM", c)); break;
            case 'B': result.Append(time.ToString("MMMM", c)); break;
            case 'c':
                result.Append(time.ToString("ddd MMM ", c)).Append(time.Day.ToString(c).PadLeft(2))
                    .Append(time.ToString(" HH:mm:ss yyyy", c));
                break;
            case 'C': result.Append((time.Year / 100).ToString("00", c)); break;
            case 'd': result.Append(time.ToString("dd", c)); break;
            case 'D': result.Append(time.ToString("MM'/'dd'/'yy", c)); break;
            case 'e': result.Append(time.Day.ToString(c).PadLeft(2)); break;
            case 'F': result.Append(time.ToString("yyyy-MM-dd", c)); break;
            case 'g': result.Append((ISOWeek.GetYear(time) % 100).ToString("00", c)); break;
            case 'G': result.Append(ISOWeek.GetYear(time).ToString(c)); break;
            case 'H': result.Append(time.ToString("HH", c)); break;
            case 'I': result.Append(time.ToString("hh", c)); break;
            case 'j': result.Append(time.DayOfYear.ToString("000", c)); break;
            case 'm': result.Append(time.ToString("MM", c)); break;
            case 'M': result.Append(time.ToString("mm", c)); break;
            case 'n': result.Append('\n'); break;
            case 'p': result.Append(time.Hour < 12 ? "AM" : "PM"); break;
            case 'r': result.Append(time.ToString("hh:mm:ss ", c)).Append(time.Hour < 12 ? "AM" : "PM"); break;
            case 'R': result.Append(time.ToString("HH:mm", c)); break;
            case 'S': result.Append(time.ToString("ss", c)); break;
            case 't': result.Append('\t'); break;
            case 'T' or 'X': result.Append(time.ToString("HH:mm:ss", c)); break;
            case 'u': result.Append(time.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)time.DayOfWeek); break;
            case 'U': result.Append(((time.DayOfYear + 6 - (int)time.DayOfWeek) / 7).ToString("00", c)); break;
            case 'V': result.Append(ISOWeek.GetWeekOfYear(time).ToString("00", c)); break;
            case 'w': result.Append((int)time.DayOfWeek); break;
            case 'W': result.Append(((time.DayOfYear + 6 - ((int)time.DayOfWeek + 6) % 7) / 7).ToString("00", c)); break;
            case 'x': result.Append(time.ToString("MM'/'dd'/'yy", c)); break;
            case 'y': result.Append(time.ToString("yy", c)); break;
            case 'Y': result.Append(time.Year.ToString(c)); break;
            case 'z':
                result.Append(offset < TimeSpan.Zero ? '-' : '+')
                    .Append(Math.Abs(offset.Hours).ToString("00", c))
                    .Append(Math.Abs(offset.Minutes).ToString("00", c));
                break;
            case 'Z': result.Append(offset == TimeSpan.Zero ? "UTC" : string.Empty); break;
            case '%': result.Append('%'); break;
            default: return false;
        }

        return true;
    }

    /// <summary>
    /// How far the local time is ahead of UTC at <paramref name="time"/>:
    /// none on a Cosmos kernel, which has no time zone, and none where the
    /// time zone cannot be read.
    /// </summary>
    private static TimeSpan LocalOffset(DateTime time)
    {
        try
        {
            return TimeZoneInfo.Local.GetUtcOffset(time);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or IOException or UnauthorizedAccessException)
        {
            return TimeSpan.Zero;
        }
    }
}
