<h1 align="center">Cosmos Lua Interpreter 🚀</h1>
<p>
  <a href="https://www.nuget.org/packages/Cosmos.Executable.Lua/" target="_blank">
    <img alt="Version" src="https://img.shields.io/nuget/v/Cosmos.Executable.Lua.svg" />
  </a>
  <a href="https://github.com/CosmosOS/Cosmos.Executable.Lua/blob/main/LICENSE.txt" target="_blank">
    <img alt="License: BSD Clause 3 License" src="https://img.shields.io/badge/license-BSD License-yellow.svg" />
  </a>
</p>

> Cosmos.Executable.Lua is a Lua 5.2 interpreter, based on [UniLua](https://github.com/xebecnan/UniLua), made in C# for the Cosmos operating system construction kit.

## Usage

Add the package to your kernel .csproj:

```xml
<ItemGroup>
    <PackageReference Include="Cosmos.Executable.Lua" Version="1.0.0" />
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

### Limitations

The standard libraries are those of Lua 5.2, except `io.popen`. Lua strings are .NET strings: a character is a UTF-16 code unit, files opened in text mode are UTF-8, and files opened in binary mode (`"rb"`, `"wb"`) map each byte to one character. There is no `__gc` and no weak tables, so close the files you open. On a Cosmos kernel the local time is UTC and `os.getenv` returns nil.

## Authors

👤 **[@xebecnan](https://github.com/xebecnan)**

👤 **[@valentinbreiz](https://github.com/valentinbreiz)**

## 🤝 Contributing

Contributions, issues and feature requests are welcome!

Feel free to check [issues page](https://github.com/CosmosOS/Cosmos.Executable.Lua/issues).

## 📝 License

Copyright © 2026 [CosmosOS](https://github.com/CosmosOS).

This project is [BSD Clause 3](https://github.com/CosmosOS/Cosmos.Executable.Lua/blob/main/LICENSE.txt) licensed. It includes UniLua, Copyright © 2013 Sheng Lunan, under the MIT license: see [THIRD-PARTY-NOTICES.txt](https://github.com/CosmosOS/Cosmos.Executable.Lua/blob/main/THIRD-PARTY-NOTICES.txt).
