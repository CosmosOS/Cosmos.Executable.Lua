// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cosmos.Executable.Lua;

/// <summary>
/// The <c>os</c> library of Lua 5.4, where UniLua had <c>os.clock</c> only,
/// on <see cref="Process"/>, which a kernel does not have.
/// </summary>
/// <remarks>
/// On a Cosmos kernel the local time is UTC, there are no environment
/// variables (<c>os.getenv</c> gives nil), and <c>os.execute</c> runs a
/// command only if the host gave the interpreter a way to
/// (<see cref="LuaInterpreter.ExecuteCommand"/>). Times are counted as a
/// 64-bit time_t counts them, with years in an <c>int</c>, and the local
/// time is told as standard time (<c>isdst</c> is false).
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
        long t1 = lua.L_CheckInteger(1);
        long t2 = lua.L_CheckInteger(2);
        lua.PushNumber((double)t1 - t2);
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

        string command = LuaText.Decode(lua.L_CheckString(1));
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
        string? value = Environment.GetEnvironmentVariable(LuaText.Decode(lua.L_CheckString(1)));
        if (value is null)
        {
            lua.PushNil();
        }
        else
        {
            lua.PushString(LuaText.Encode(value));
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
                lua.PushString(LuaText.Encode(name));
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

    /// <summary>
    /// The conversions <c>os.date</c> accepts, as LUA_STRFTIMEOPTIONS has
    /// them for C99: those of one character, then, after <c>||</c>, those of
    /// two.
    /// </summary>
    private const string StrftimeOptions = "aAbBcCdDeFgGhHIjmMnprRStTuUVwWxXyYzZ%" + "||" + "EcECExEXEyEY" + "OdOeOHOIOmOMOSOuOUOVOwOWOy";

    /// <summary>
    /// The times <see cref="TimeZoneInfo"/> gives the offsets of: those of
    /// <see cref="DateTime"/>, a day in from its ends. A later time takes the
    /// offset of the same time 400 years, or a multiple of them, earlier (the
    /// calendar repeats, and so do the rules of the time zone); an earlier
    /// time the offset of the first.
    /// </summary>
    private const long MinZoneTime = -62135596800 + 86400;
    private const long MaxZoneTime = 253402300799 - 86400;

    private const long SecondsPerDay = 86400;

    /// <summary>The seconds of 400 years of the Gregorian calendar, which then repeats, weekdays and all.</summary>
    private const long GregorianCycle = 146097 * SecondsPerDay;

    private static readonly string[] DayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    /// <summary>
    /// struct tm: a time broken down into its fields, as gmtime and
    /// localtime give it and mktime takes it, with the offset and the name of
    /// its time zone (tm_gmtoff and tm_zone).
    /// </summary>
    private struct Tm
    {
        public int Sec;
        public int Min;
        public int Hour;
        public int MDay;

        /// <summary>Months since January, 0 to 11.</summary>
        public int Mon;

        /// <summary>Years since 1900.</summary>
        public int Year;

        /// <summary>Days since Sunday, 0 to 6.</summary>
        public int WDay;

        /// <summary>Days since January 1, 0 to 365.</summary>
        public int YDay;

        /// <summary>Seconds east of UTC.</summary>
        public long GmtOff;

        public string Zone;
    }

    private static int OS_Time(ILuaState lua)
    {
        long t;
        if (lua.IsNoneOrNil(1))
        {
            // called without args: the current time
            t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        else
        {
            lua.L_CheckType(1, LuaType.LUA_TTABLE);
            lua.SetTop(1); // make sure table is at the top
            Tm ts = default;
            ts.Year = GetField(lua, "year", -1, 1900);
            ts.Mon = GetField(lua, "month", -1, 1);
            ts.MDay = GetField(lua, "day", -1, 0);
            ts.Hour = GetField(lua, "hour", 12, 0);
            ts.Min = GetField(lua, "min", 0, 0);
            ts.Sec = GetField(lua, "sec", 0, 0);
            GetBoolField(lua, "isdst"); // read, but the rules of the zone alone say whether daylight saving time applies
            if (MkTime(ref ts, out t))
            {
                SetAllFields(lua, ts); // update fields with normalized values
            }
            else
            {
                t = -1; // what mktime gives when the year of the result does not fit
            }
        }

        if (t == -1)
        {
            return lua.L_Error("time result cannot be represented in this installation");
        }

        lua.PushInteger(t);
        return 1;
    }

    /// <summary>
    /// getfield: a field of the date table, an integer that must fit in the
    /// <c>int</c> of struct tm once <paramref name="delta"/> is taken from it,
    /// or else the default <paramref name="d"/> if there is one.
    /// </summary>
    private static int GetField(ILuaState lua, string key, int d, int delta)
    {
        LuaType t = lua.GetField(-1, key); // get field and its type
        long res = lua.ToIntegerX(-1, out bool isNum);
        if (!isNum)
        {
            // field is not an integer?
            if (t != LuaType.LUA_TNIL) // some other value?
            {
                return lua.L_Error("field '{0}' is not an integer", key);
            }

            if (d < 0) // absent field; no default?
            {
                return lua.L_Error("field '{0}' missing in date table", key);
            }

            res = d;
        }
        else
        {
            if (!(res >= 0 ? res - delta <= int.MaxValue : int.MinValue + delta <= res))
            {
                return lua.L_Error("field '{0}' is out-of-bound", key);
            }

            res -= delta;
        }

        lua.Pop(1);
        return (int)res;
    }

    /// <summary>getboolfield: -1 for an absent field, or else whether it is true.</summary>
    private static int GetBoolField(ILuaState lua, string key)
    {
        int res = lua.GetField(-1, key) == LuaType.LUA_TNIL ? -1 : (lua.ToBoolean(-1) ? 1 : 0);
        lua.Pop(1);
        return res;
    }

    /// <summary>setallfields: sets the fields of the table on top of the stack from <paramref name="stm"/>.</summary>
    private static void SetAllFields(ILuaState lua, in Tm stm)
    {
        SetField(lua, "year", stm.Year, 1900);
        SetField(lua, "month", stm.Mon, 1);
        SetField(lua, "day", stm.MDay, 0);
        SetField(lua, "hour", stm.Hour, 0);
        SetField(lua, "min", stm.Min, 0);
        SetField(lua, "sec", stm.Sec, 0);
        SetField(lua, "yday", stm.YDay, 1);
        SetField(lua, "wday", stm.WDay, 1);
        // tm_isdst: the local time is told as standard time
        lua.PushBoolean(false);
        lua.SetField(-2, "isdst");
    }

    private static void SetField(ILuaState lua, string key, int value, int delta)
    {
        lua.PushInteger((long)value + delta);
        lua.SetField(-2, key);
    }

    private static int OS_Date(ILuaState lua)
    {
        string s = lua.L_OptString(1, "%c");
        long t = lua.IsNoneOrNil(2) ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : lua.L_CheckInteger(2); // l_checktime
        int i = 0;
        bool valid;
        Tm stm;
        if (s.Length > 0 && s[0] == '!')
        {
            // UTC?
            valid = GmTime(t, out stm);
            i++; // skip '!'
        }
        else
        {
            valid = LocalTime(t, out stm);
        }

        if (!valid)
        {
            // invalid date?
            return lua.L_Error("date result cannot be represented in this installation");
        }

        if (CString(s, i) == "*t")
        {
            lua.CreateTable(0, 9); // 9 = number of fields
            SetAllFields(lua, stm);
            return 1;
        }

        StringBuilder b = new();
        while (i < s.Length)
        {
            if (s[i] != '%')
            {
                // not a conversion specifier?
                b.Append(s[i++]);
            }
            else
            {
                i++; // skip '%'
                i = CheckOption(lua, s, i, out string conversion);
                StrFTime(b, conversion[^1], stm); // the C locale has no alternative forms for E and O
            }
        }

        lua.PushString(b.ToString());
        return 1;
    }

    /// <summary>
    /// checkoption: the conversion at <paramref name="conv"/> in
    /// <paramref name="s"/>, which must be one of
    /// <see cref="StrftimeOptions"/>; the index of the item after it.
    /// </summary>
    private static int CheckOption(ILuaState lua, string s, int conv, out string option)
    {
        int convlen = s.Length - conv;
        int oplen = 1; // length of options being checked
        for (int o = 0; o < StrftimeOptions.Length && oplen <= convlen; o += oplen)
        {
            if (StrftimeOptions[o] == '|')
            {
                // next block?
                oplen++; // will check options with next length (+1)
            }
            else if (string.CompareOrdinal(s, conv, StrftimeOptions, o, oplen) == 0)
            {
                // match?
                option = s.Substring(conv, oplen); // copy valid option
                return conv + oplen; // return next item
            }
        }

        option = string.Empty;
        lua.L_ArgError(1, "invalid conversion specifier '%" + CString(s, conv) + "'");
        return conv;
    }

    /// <summary>The C string at <paramref name="start"/> in <paramref name="s"/>: up to its first zero.</summary>
    private static string CString(string s, int start)
    {
        int end = s.IndexOf('\0', start);
        return end < 0 ? s[start..] : s[start..end];
    }

    /// <summary>
    /// Appends one conversion of strftime as the C library of the reference
    /// implementation (glibc) writes it in the C locale.
    /// </summary>
    private static void StrFTime(StringBuilder b, char conversion, in Tm tm)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        long year = 1900L + tm.Year;
        switch (conversion)
        {
            case 'a': b.Append(DayNames[tm.WDay], 0, 3); break;
            case 'A': b.Append(DayNames[tm.WDay]); break;
            case 'b' or 'h': b.Append(MonthNames[tm.Mon], 0, 3); break;
            case 'B': b.Append(MonthNames[tm.Mon]); break;
            case 'c': Compose(b, "%a %b %e %H:%M:%S %Y", tm); break;
            case 'C': b.Append(FloorDiv(year, 100).ToString(c)); break;
            case 'd': Number(b, tm.MDay, 2); break;
            case 'D' or 'x': Compose(b, "%m/%d/%y", tm); break;
            case 'e': b.Append(tm.MDay.ToString(c).PadLeft(2)); break;
            case 'F': Compose(b, "%Y-%m-%d", tm); break;
            case 'g': Number(b, FloorMod(IsoYear(tm, out _), 100), 2); break;
            case 'G': b.Append(IsoYear(tm, out _).ToString(c)); break;
            case 'H': Number(b, tm.Hour, 2); break;
            case 'I': Number(b, tm.Hour % 12 == 0 ? 12 : tm.Hour % 12, 2); break;
            case 'j': Number(b, tm.YDay + 1, 3); break;
            case 'm': Number(b, tm.Mon + 1, 2); break;
            case 'M': Number(b, tm.Min, 2); break;
            case 'n': b.Append('\n'); break;
            case 'p': b.Append(tm.Hour < 12 ? "AM" : "PM"); break;
            case 'r': Compose(b, "%I:%M:%S %p", tm); break;
            case 'R': Compose(b, "%H:%M", tm); break;
            case 'S': Number(b, tm.Sec, 2); break;
            case 't': b.Append('\t'); break;
            case 'T' or 'X': Compose(b, "%H:%M:%S", tm); break;
            case 'u': b.Append((tm.WDay - 1 + 7) % 7 + 1); break;
            case 'U': Number(b, (tm.YDay - tm.WDay + 7) / 7, 2); break;
            case 'V':
                IsoYear(tm, out int days);
                Number(b, days / 7 + 1, 2);
                break;
            case 'w': b.Append(tm.WDay); break;
            case 'W': Number(b, (tm.YDay - (tm.WDay - 1 + 7) % 7 + 7) / 7, 2); break;
            case 'y': Number(b, FloorMod(year, 100), 2); break;
            case 'Y': b.Append(year.ToString(c)); break;
            case 'z':
                long minutes = Math.Abs(tm.GmtOff) / 60;
                b.Append(tm.GmtOff < 0 ? '-' : '+');
                Number(b, minutes / 60 * 100 + minutes % 60, 4);
                break;
            case 'Z': b.Append(tm.Zone); break;
            case '%': b.Append('%'); break;
        }
    }

    /// <summary>Appends the conversions of <paramref name="format"/>, which stands for one, such as <c>%T</c>.</summary>
    private static void Compose(StringBuilder b, string format, in Tm tm)
    {
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] == '%')
            {
                StrFTime(b, format[++i], tm);
            }
            else
            {
                b.Append(format[i]);
            }
        }
    }

    private static void Number(StringBuilder b, long value, int digits)
    {
        b.Append(value.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0'));
    }

    /// <summary>
    /// The ISO 8601 year of the week of <paramref name="tm"/>, and in
    /// <paramref name="days"/> the days since the start of its first week,
    /// as glibc's iso_week_days counts them.
    /// </summary>
    private static long IsoYear(in Tm tm, out int days)
    {
        long year = 1900L + tm.Year;
        days = IsoWeekDays(tm.YDay, tm.WDay);
        if (days < 0)
        {
            // This ISO week belongs to the previous year
            days = IsoWeekDays(tm.YDay + 365 + (IsLeap(year - 1) ? 1 : 0), tm.WDay);
            return year - 1;
        }

        int d = IsoWeekDays(tm.YDay - (365 + (IsLeap(year) ? 1 : 0)), tm.WDay);
        if (0 <= d)
        {
            // This ISO week belongs to the next year
            days = d;
            return year + 1;
        }

        return year;
    }

    /// <summary>The days between the start of the first ISO week of the year (Monday) and the day <paramref name="yday"/>.</summary>
    private static int IsoWeekDays(int yday, int wday)
    {
        // Add enough to the first operand of % to make it nonnegative
        const int BigEnoughMultipleOf7 = (366 / 7 + 2) * 7;
        return yday - (yday - wday + 4 + BigEnoughMultipleOf7) % 7 + 4 - 1;
    }

    private static bool IsLeap(long year)
    {
        return year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
    }

    /// <summary>gmtime: the fields of the time <paramref name="t"/> in UTC; false when its year does not fit in an <c>int</c>.</summary>
    private static bool GmTime(long t, out Tm tm)
    {
        return BreakDown(t, 0, "GMT", out tm);
    }

    /// <summary>localtime: the fields of the time <paramref name="t"/> in the local time zone; see <see cref="GmTime"/>.</summary>
    private static bool LocalTime(long t, out Tm tm)
    {
        long offset = UtcOffset(t);
        return BreakDown(t, offset, offset == 0 ? "UTC" : string.Empty, out tm);
    }

    private static bool BreakDown(long t, long offset, string zone, out Tm tm)
    {
        tm = default;
        if (offset > 0 ? t > long.MaxValue - offset : t < long.MinValue - offset)
        {
            return false;
        }

        long local = t + offset;
        long days = FloorDiv(local, SecondsPerDay);
        long seconds = local - days * SecondsPerDay;
        long year = CivilFromDays(days, out int month, out int day);
        if (year - 1900 > int.MaxValue || year - 1900 < int.MinValue)
        {
            return false; // EOVERFLOW
        }

        tm.Year = (int)(year - 1900);
        tm.Mon = month - 1;
        tm.MDay = day;
        tm.Hour = (int)(seconds / 3600);
        tm.Min = (int)(seconds / 60 % 60);
        tm.Sec = (int)(seconds % 60);
        tm.WDay = (int)FloorMod(days + 4, 7); // January 1, 1970 was a Thursday
        tm.YDay = (int)(days - DaysFromCivil(year, 1, 1));
        tm.GmtOff = offset;
        tm.Zone = zone;
        return true;
    }

    /// <summary>
    /// mktime: the time the local fields of <paramref name="tm"/> name,
    /// whichever their range (60 seconds is the next minute, day 0 the last
    /// of the month before), and the fields normalized; false, and the fields
    /// as they were, when the year of the result does not fit in an <c>int</c>.
    /// </summary>
    private static bool MkTime(ref Tm tm, out long t)
    {
        // As ints, the fields cannot take the count of seconds out of a long
        long year = 1900L + tm.Year + FloorDiv(tm.Mon, 12);
        int month = (int)FloorMod(tm.Mon, 12) + 1;
        long days = DaysFromCivil(year, month, 1) + tm.MDay - 1;
        long local = days * SecondsPerDay + tm.Hour * 3600L + tm.Min * 60L + tm.Sec;
        t = local - LocalOffset(local);
        if (!LocalTime(t, out Tm normalized))
        {
            return false;
        }

        tm = normalized;
        return true;
    }

    /// <summary>The days from January 1, 1970 to the date, in the proleptic Gregorian calendar.</summary>
    private static long DaysFromCivil(long year, int month, int day)
    {
        year -= month <= 2 ? 1 : 0;
        long era = FloorDiv(year, 400);
        long yearOfEra = year - era * 400; // [0, 399]
        long dayOfYear = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1; // [0, 365], from March 1
        long dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear; // [0, 146096]
        return era * 146097 + dayOfEra - 719468;
    }

    /// <summary>The date <paramref name="days"/> days from January 1, 1970: its year, month and day.</summary>
    private static long CivilFromDays(long days, out int month, out int day)
    {
        days += 719468; // from March 1 of year 0
        long era = FloorDiv(days, 146097);
        long dayOfEra = days - era * 146097; // [0, 146096]
        long yearOfEra = (dayOfEra - dayOfEra / 1460 + dayOfEra / 36524 - dayOfEra / 146096) / 365; // [0, 399]
        long dayOfYear = dayOfEra - (365 * yearOfEra + yearOfEra / 4 - yearOfEra / 100); // [0, 365]
        long mp = (5 * dayOfYear + 2) / 153; // [0, 11], from March
        day = (int)(dayOfYear - (153 * mp + 2) / 5 + 1);
        month = (int)(mp < 10 ? mp + 3 : mp - 9);
        return yearOfEra + era * 400 + (month <= 2 ? 1 : 0);
    }

    private static long FloorDiv(long a, long b)
    {
        long q = a / b;
        return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
    }

    private static long FloorMod(long a, long b)
    {
        return a - FloorDiv(a, b) * b;
    }

    /// <summary>
    /// How far the local time is ahead of UTC at the time <paramref name="t"/>,
    /// in seconds: none on a Cosmos kernel, which has no time zone, and none
    /// where the time zone cannot be read.
    /// </summary>
    private static long UtcOffset(long t)
    {
        return ZoneOffset(new DateTime(ZoneTicks(t), DateTimeKind.Utc));
    }

    /// <summary>How far the local time <paramref name="local"/> (in seconds from 1970, as UTC would count them) is ahead of UTC.</summary>
    private static long LocalOffset(long local)
    {
        return ZoneOffset(new DateTime(ZoneTicks(local), DateTimeKind.Unspecified));
    }

    /// <summary>The ticks of a <see cref="DateTime"/> whose offset is that of the time <paramref name="t"/>; see <see cref="MinZoneTime"/>.</summary>
    private static long ZoneTicks(long t)
    {
        if (t > MaxZoneTime)
        {
            t -= ((t - MaxZoneTime - 1) / GregorianCycle + 1) * GregorianCycle;
        }
        else if (t < MinZoneTime)
        {
            t = MinZoneTime;
        }

        return DateTime.UnixEpoch.Ticks + t * TimeSpan.TicksPerSecond;
    }

    private static long ZoneOffset(DateTime time)
    {
        try
        {
            return (long)TimeZoneInfo.Local.GetUtcOffset(time).TotalSeconds;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
