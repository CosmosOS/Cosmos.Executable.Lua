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
    <PackageReference Include="Cosmos.Executable.Lua" Version="4.0.1" />
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

As in C Lua, a Lua string is a sequence of bytes, not characters. Text is stored as UTF-8, so `#"é"` is 2, and the `utf8` library works as expected.

Most of the time you don't need to care: `LuaInterpreter` converts the code, arguments and error messages for you, and so do the console, file names and `os.getenv`. Files are read and written byte for byte, in text and binary mode.

You only see the bytes in your own C# functions. On `ILuaState`, a Lua string is a .NET string with one character (`\0` to `\xFF`) per byte. Use `LuaText` to convert:

```csharp
state.PushString(LuaText.Encode("héllo")); // the 6 bytes of "héllo"
string text = LuaText.Decode(state.ToString(-1)); // "héllo" again
```

### Limitations

The interpreter behaves like the reference build of Lua 5.5 and passes the official [Lua 5.5 test suite](https://www.lua.org/tests/) (lua-5.5.1-tests), unmodified.

What is different:

- `io.popen` is not supported.
- The garbage collector always runs full cycles. Weak tables, `__gc` and `collectgarbage("count")` work as usual, but the incremental and generational modes and `collectgarbage("param")` only change how often a cycle runs.
- On Cosmos, the local time is UTC, `os.getenv` returns nil, and `os.tmpname` fails because the kernel has no `/tmp` yet.

Note that files are buffered as in C: a write reaches the file only when the buffer is full, on `flush`, or when the file is closed.

## Authors

👤 **[@xebecnan](https://github.com/xebecnan)**

👤 **[@valentinbreiz](https://github.com/valentinbreiz)**

## 🤝 Contributing

Contributions, issues and feature requests are welcome!

Feel free to check [issues page](https://github.com/CosmosOS/Cosmos.Executable.Lua/issues).

## 📝 License

Copyright © 2026 [CosmosOS](https://github.com/CosmosOS).

This project is [BSD Clause 3](https://github.com/CosmosOS/Cosmos.Executable.Lua/blob/main/LICENSE.txt) licensed. It includes UniLua, Copyright © 2013 Sheng Lunan, and code ported from Lua 5.3, Lua 5.4 and Lua 5.5, Copyright © 1994–2026 Lua.org, PUC-Rio, both under the MIT license: see [THIRD-PARTY-NOTICES.txt](https://github.com/CosmosOS/Cosmos.Executable.Lua/blob/main/THIRD-PARTY-NOTICES.txt).
