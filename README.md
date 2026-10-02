<h1 align="center">Cosmos Lua Interpreter 🚀</h1>
<p>
  <a href="https://www.nuget.org/packages/Cosmos.Executable.Lua/" target="_blank">
    <img alt="Version" src="https://img.shields.io/nuget/v/Cosmos.Executable.Lua.svg" />
  </a>
  <a href="https://github.com/CosmosOS/Cosmos.Executable.Lua/blob/main/LICENSE.txt" target="_blank">
    <img alt="License: BSD Clause 3 License" src="https://img.shields.io/badge/license-BSD License-yellow.svg" />
  </a>
</p>

> Cosmos.Executable.Lua is a Lua 5.4 interpreter, based on [UniLua](https://github.com/xebecnan/UniLua), made in C# for the Cosmos operating system construction kit.

## Usage

Add the package to your kernel .csproj:

```xml
<ItemGroup>
    <PackageReference Include="Cosmos.Executable.Lua" Version="3.0.0" />
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

The standard libraries are those of Lua 5.4 built with `LUA_COMPAT_5_3`, as the reference one is, so `math.pow` and the others are there, except `io.popen`. There is no `__gc` and no weak tables, so close the files you open, or give them to a `<close>` variable: a write reaches the file at once, unless `file:setvbuf` asks for a buffer. `collectgarbage` asks .NET for a collection; its other options change nothing. C calls and the parser nest 150 levels deep, not 200, for the small stacks of a kernel's threads. On a Cosmos kernel the local time is UTC and `os.getenv` returns nil.

The tests run the official [Lua 5.4 test suite](https://www.lua.org/tests/) (lua-5.4.9-tests), each file alone and then all together through its `all.lua`, which loads them again from `string.dump`: all of it but `gc.lua` and `gengc.lua`, with the lines these limitations break patched in `LuaTestSuiteTests.cs`.

## Authors

👤 **[@xebecnan](https://github.com/xebecnan)**

👤 **[@valentinbreiz](https://github.com/valentinbreiz)**

## 🤝 Contributing

Contributions, issues and feature requests are welcome!

Feel free to check [issues page](https://github.com/CosmosOS/Cosmos.Executable.Lua/issues).

## 📝 License

Copyright © 2026 [CosmosOS](https://github.com/CosmosOS).

This project is [BSD Clause 3](https://github.com/CosmosOS/Cosmos.Executable.Lua/blob/main/LICENSE.txt) licensed. It includes UniLua, Copyright © 2013 Sheng Lunan, and code ported from Lua 5.3 and Lua 5.4, Copyright © 1994–2026 Lua.org, PUC-Rio, both under the MIT license: see [THIRD-PARTY-NOTICES.txt](https://github.com/CosmosOS/Cosmos.Executable.Lua/blob/main/THIRD-PARTY-NOTICES.txt).
