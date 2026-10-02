<h1 align="center">Cosmos Lua Interpreter 🚀</h1>
<p>
  <a href="https://www.nuget.org/packages/Cosmos.Executable.Lua/" target="_blank">
    <img alt="Version" src="https://img.shields.io/nuget/v/Cosmos.Executable.Lua.svg" />
  </a>
  <a href="https://github.com/CosmosOS/Cosmos.Executable.Lua/blob/main/LICENSE.txt" target="_blank">
    <img alt="License: BSD Clause 3 License" src="https://img.shields.io/badge/license-BSD License-yellow.svg" />
  </a>
</p>

> Cosmos.Executable.Lua is a Lua 5.5 interpreter, based on [UniLua](https://github.com/xebecnan/UniLua), made in C# for the Cosmos operating system construction kit.

## Usage

Add the package to your kernel .csproj:

```xml
<ItemGroup>
    <PackageReference Include="Cosmos.Executable.Lua" Version="4.0.0" />
</ItemGroup>
```

```csharp
using System;
using Cosmos.Executable.Lua;

LuaInterpreter lua = new()
{
    WorkingDirectory = "/mnt", // where dofile, require and io.open start relative paths from
};

try
{
    lua.DoString("print('Hello from ' .. _VERSION)");
    lua.DoFile("script.lua", "first argument"); // as `lua script.lua first argument`
    lua.RunPrompt(); // the interactive prompt, until os.exit()
}
catch (LuaException e)
{
    // A syntax error, or a runtime error no pcall caught
    Console.WriteLine(e.Message);
    Console.WriteLine(e.LuaStackTrace);
}
catch (LuaExitException e)
{
    // The script called os.exit(e.ExitCode)
}
```

A C# function raises a Lua error with `state.L_Error(...)`, or by throwing: a .NET exception becomes a Lua error that `pcall` catches.

### Strings

Lua strings hold bytes, as in C Lua: on the `ILuaState` API a Lua string is a .NET string with one character, `\0` to `\xFF`, per byte. `LuaInterpreter` takes and gives text, as UTF-8, and so do the console, file names and `os.getenv`; files give and take their bytes as they are, in text mode as in binary mode. So `#"é"` is 2, and the `utf8` library reads the bytes of UTF-8 text. A C# function converts text with `LuaText`:

```csharp
state.PushString(LuaText.Encode("héllo")); // the 6 bytes of "héllo"
string text = LuaText.Decode(state.ToString(-1)); // "héllo" again
```

### Limitations

The language and the standard libraries are those of Lua 5.5 as the reference build makes them, except `io.popen`: `global` is a reserved word only where it starts a declaration (`LUA_COMPAT_GLOBAL`), and `math.pow` and the other deprecated functions are gone. Files are buffered as C's are: a write reaches the file when the buffer fills, on `flush`, or when the script, the collector or the end of the interpreter closes the file. On a Cosmos kernel the local time is UTC, `os.getenv` returns nil, and `os.tmpname` fails, as the kernel has no `/tmp` yet.

The state runs the collector of Lua 5.5 over its own objects, so weak tables, `__gc` finalizers and `collectgarbage("count")` behave as in the reference implementation, and the .NET collector, the kernel's on Cosmos, frees what it lets go. Each cycle is a whole one: the incremental and generational modes, and the parameters `collectgarbage("param")` sets, only pace the cycles.

The tests run the official [Lua 5.5 test suite](https://www.lua.org/tests/) (lua-5.5.1-tests), as its authors wrote it, each file alone and then all together through its `all.lua`, which loads them again from `string.dump`.

## Authors

👤 **[@xebecnan](https://github.com/xebecnan)**

👤 **[@valentinbreiz](https://github.com/valentinbreiz)**

## 🤝 Contributing

Contributions, issues and feature requests are welcome!

Feel free to check [issues page](https://github.com/CosmosOS/Cosmos.Executable.Lua/issues).

## 📝 License

Copyright © 2026 [CosmosOS](https://github.com/CosmosOS).

This project is [BSD Clause 3](https://github.com/CosmosOS/Cosmos.Executable.Lua/blob/main/LICENSE.txt) licensed. It includes UniLua, Copyright © 2013 Sheng Lunan, and code ported from Lua 5.3, Lua 5.4 and Lua 5.5, Copyright © 1994–2026 Lua.org, PUC-Rio, both under the MIT license: see [THIRD-PARTY-NOTICES.txt](https://github.com/CosmosOS/Cosmos.Executable.Lua/blob/main/THIRD-PARTY-NOTICES.txt).
