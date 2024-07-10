# DXCompiler.NET: cross-platform C# wrapper for DirectXShaderCompiler 

A cross-platform .NET wrapper written in C# to enable compiling HLSL code using Microsoft's DirectX Shader Compiler. 

# Usage

This project wraps functionality from DXC into a managed DirectXShaderCompiler class, which can be used to easily compile shader code with various options.<br>
The following is a short example showcasing how a shader can be compiled using this wrapper, along with how source file inclusion can be overridden from C#.

```cs
using DXCompiler.NET;

public class Program
{
    const string HlslCode = @"
#include ""IncludedFuncFile.hlsl""

struct PixelInput {
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
};

float4 pixel(VertexOutput input) : SV_Target { 
    input.Color *= 10;
    input.Color /= INCLUDED_FUNC(input.Color.r);
    return input.Color;
}
";

    public static string IncludeFile(string filename)
    {
        Console.WriteLine($"Including file: {filename}");

        // Instead of loading a file, add a placeholder preprocessor macro.
        return "#define INCLUDED_FUNC(x) x * 10 - sin(x)";
    }

    public static void Main(string[] args)
    {
        // Define the shader profile being targeted. In this case the shader is a 6.0 pixel (fragment) shader.
        ShaderProfile profile = new ShaderProfile(ShaderType.Pixel, 6, 0);

        // Define the compilation options
        CompilerOptions options = new CompilerOptions(profile)
        {
            entryPoint = "pixel", // The entry-point function. Entry point defaults to 'main' when not specified.
            generateAsSpirV = true, // Generate SPIR-V bytecode instead of DXIL.
        };

        // Log the options as the command-line arguments that DXC consumes.  
        Console.WriteLine(string.Join(' ', options.GetArgumentsArray()));

        // Create an instance of DXShaderCompiler, ensuring it is properly disposed of with a `using` statement.
        using DXShaderCompiler compiler = new DXShaderCompiler();

        // Compile the shader with our shader code, options, and a custom include handler.
        CompilationResult result = compiler.Compile(HlslCode, options, IncludeFile);

        // Compilation errors exist, log them and exit.
        if (result.compilationErrors != null)
        {
            Console.WriteLine("Errors compiling shader:");
            Console.WriteLine(result.compilationErrors);
            return;
        }

        // Compilation succeded, log how many bytes were generated.
        Console.WriteLine($"Success! {result.objectBytes.Length} bytes generated.");
    }
}
```

# API Reference

### DirectXShaderCompiler.NET
The namespace all the following types are contained under.

### DirectXShaderCompiler
The main instance of DXC to be used by the program. This class should be treated as a singleton, and only one instance may be active at any given part of the program's lifetime.<br>
This class inherits from IDisposable, and its lifetime should be properly managed with a `using` statement or by manually calling `Dispose` when it is no longer required.

- Methods
  - `Compile(string code, string[] compilationOptions, FileIncludeHandler includeHandler)`
    - Returns `CompilationResult`.
    - Accepts a list of DXC command-line compilation options formatted as specified [here](https://github.com/microsoft/DirectXShaderCompiler/wiki/Using-dxc.exe-and-dxcompiler.dll)
    - Compiles a string of shader code, outputting a CompilationResult containing the bytecode and any compiler errors.
      
  - `Compile(string code, CompilerOptions compilationOptions, FileIncludeHandler includeHandler)`
    - Returns `CompilationResult`.
    - Accepts a CompilerOptions object.
    - Compiles a string of shader code, outputting a CompilationResult containing the bytecode and any compiler errors.
   

### CompilationResult
The compilation result returned by `DirectXShaderCompiler.Compile()`

- Fields
  - `objectBytes`:`byte[]`
    - Contains the output bytes from compilation.
  - `compilationErrors`:`string?`
    - Contains a compiler error message if compilation failed, otherwise null.

### CompilerOptions
The compilation options used by `DirectXShaderCompiler.Compile()`<br>
This class contains nearly all command-line options specified by DXC.exe. Most options are under aliases to match C# naming conventions, and can be set as usual C# fields.<br><br>**DISCLAIMER:** CompilerOptions does not sanitize or validate certain combinations of options which can break DXC and cause segmentation faults. 

- Methods
  - `new CompilerOptions(ShaderProfile profile) (Constructor)`
    - Returns a new instance of `CompilerOptions` with the given shader profile.
  - `SetMacro(string name, string value)`
    - Sets a compilation macro for the compiler to use during the preprocessing step.
  - `RemoveMacro(string name)`
    - Removes a compilation macro.
  - `SetWarning(string warning, bool enabled)`
    - Explicitly enables or disables a warning.
   
- Fields<br>
  CompilerOptions has a numerous amount of fields under aliases, and not all of them will be listed here. To see a list of all the fields, please reference the source `CompilerOptions.cs` file.

### ShaderProfile
An abstraction over the shader profile inputs that DXC accepts. 

- Methods
  - `new ShaderProfile(ShaderType type, int version = 6, int subVersion = 0) (Constructor)`
    - Returns a new instance of `ShaderProfile` with the given type and version.

- Fields<br>
  - `Type`:`enum`
    - The type of this shader profile.
  - `Version`:`int`
    - The major version of this shader profile. Minimum major version compiled by DXC is 6.
  - `SubVersion`:`int`
    - The minor version of this shader profile. Clamped between 0-8.
### ShaderType
An enum denoting the different shader types accepted by DXC.<br>
Values include:
  - Pixel
  - Vertex
  - Hull
  - Geometry
  - Domain
  - Compute
  - Library
  - Mesh
  - Amplification

# Native Details

Due to the nature of the official DirectXShaderCompiler repository's build system, it is difficult to easily acquire or build binaries of DXC that meets the following criteria:<br>

- Exposes a cross-platform c-style interface for DXC.
- Runs on all major desktop operating systems.
- Can be compiled for all major desktop operating systems from any system.

As such, DirectXShaderCompiler.NET uses a fork of DXC known as [mach-dxcompiler](https://github.com/sinnwrig/mach-dxcompiler), built using the Zig build system instead of CMake. As Zig's compiler supports cross-compilation out of the box, it allows DXC to build on any platform, for any platform. With minor tweaking, additional functionality also allows mach-dxcompiler to be built as a shared library and with SPIR-V support, these library binaries being what DirectXShaderCompiler.NET uses in its release.

## Building Native Libraries

To build native libraries, run the `BuildNative.cs` file inside the _Native_ folder, specicying your target architecture [X64, ARM64, All] with -A and your target platform [Windows, Linux, MacOS, All] with -P.<br>

Native build requirements:
- Zig prerelease version of at least 0.14.0

Pre-built binaries are bundled in the NuGet package for the following operating systems:
- Windows x64
- Windows arm64
- OSX x64
- OSX arm64
- Linux x64
- Linux arm64