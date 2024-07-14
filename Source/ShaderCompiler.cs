using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DirectXShaderCompiler.NET;

/// <summary>
/// A delegate for shader header inclusion.
/// </summary>
/// <param name="includeName">The name of the header file being included.</param>
/// <returns>Contents of the included header file.</returns>
public delegate string FileIncludeHandler(string includeName);

/// <summary>
/// A static class that allows accessing shader compilation functionality found in the DirectXShaderCompiler. 
/// </summary>
public static class ShaderCompiler
{
    private static unsafe NativeDxcCompiler* nativeCompiler = DXCNative.DxcInitialize();

    private static unsafe void* includePtr = (void*)Marshal.GetFunctionPointerForDelegate<NativeDxcIncludeFunction>(Include);
    private static unsafe NativeDxcIncludeResult* Include(IntPtr nativeContext, char* headerUtf16)
    {
        if (nativeContext == IntPtr.Zero)
            return null;

        string? headerName = Marshal.PtrToStringUni((IntPtr)headerUtf16);

        if (headerName != null)
        {
            FileIncludeHandler handler = Marshal.GetDelegateForFunctionPointer<FileIncludeHandler>(nativeContext);
            string includeFile = handler.Invoke(headerName);

            NativeDxcIncludeResult includeResult = new NativeDxcIncludeResult
            {
                header_data = NativeStringUtility.GetUTF16Ptr(includeFile, out uint len, false),
                header_length = len
            };

            return (NativeDxcIncludeResult*)AllocStruct(includeResult);
        }

        return null;
    }

    private static unsafe void* freeIncludePtr = (void*)Marshal.GetFunctionPointerForDelegate<NativeDxcFreeIncludeFunction>(FreeInclude);
    private static unsafe int FreeInclude(IntPtr nativeContext, NativeDxcIncludeResult* includeResult)
    {
        if (includeResult == null)
            return 0;

        NativeDxcIncludeResult result = Marshal.PtrToStructure<NativeDxcIncludeResult>((IntPtr)includeResult);

        Marshal.FreeHGlobal((IntPtr)result.header_data);
        Marshal.FreeHGlobal((IntPtr)includeResult);

        return 0;
    }

    private static unsafe IntPtr AllocStruct<T>(T type) where T : struct
    {
        IntPtr memPtr = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        Marshal.StructureToPtr(type, memPtr, false);
        return memPtr;
    }

    /// <summary>
    /// Compiles a string of shader code.
    /// </summary>
    /// <param name="code">The code to compile.</param>
    /// <param name="compilationOptions">The options to compile with.</param>
    /// <param name="includeHandler">The include handler to use for header inclusion.</param>
    /// <returns>A CompilationResult structure containing the resulting bytecode and possible errors.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CompilationResult Compile(string code, CompilerOptions compilationOptions, FileIncludeHandler? includeHandler = null)
    {
        return Compile(code, compilationOptions.GetArgumentsArray(), includeHandler);
    }

    /// <summary>
    /// Compiles a string of shader code.
    /// </summary>
    /// <param name="code">The code string to compile.</param>
    /// <param name="compilerArgs">The array of string arguments to compile the shader with.</param>
    /// <param name="includeHandler">The include handler to use for header inclusion.</param>
    /// <returns>A CompilationResult structure containing the resulting bytecode and possible errors.</returns>
    public unsafe static CompilationResult Compile(string code, string[] compilerArgs, FileIncludeHandler? includeHandler = null)
    {   
        GCHandle codePinned = GCHandle.Alloc(code, GCHandleType.Pinned);

        Span<GCHandle> argsPinned = stackalloc GCHandle[compilerArgs.Length];

        char** argsUtf8 = stackalloc char*[compilerArgs.Length];

        for (int i = 0; i < compilerArgs.Length; i++)
        {
            GCHandle pin = GCHandle.Alloc(compilerArgs[i], GCHandleType.Pinned); // Pin argument

            argsPinned[i] = pin;
            argsUtf8[i] = (char*)pin.AddrOfPinnedObject();
        }

        NativeDxcIncludeCallbacks* callbacks = stackalloc NativeDxcIncludeCallbacks[1];

        if (includeHandler != null)
            callbacks->include_ctx = (void*)Marshal.GetFunctionPointerForDelegate(includeHandler);
        else
            callbacks->include_ctx = null;

        callbacks->include_func = includePtr;
        callbacks->free_func = freeIncludePtr;

        NativeDxcCompileOptions* options = stackalloc NativeDxcCompileOptions[1];

        options->code = (char*)codePinned.AddrOfPinnedObject();
        options->code_len = (nuint)code.Length;
        options->args = argsUtf8;
        options->args_len = (nuint)compilerArgs.Length;
        options->include_callbacks = callbacks;
    
        CompilationResult result = GetResult(DXCNative.DxcCompile(nativeCompiler, options));

        codePinned.Free();

        for (int i = 0; i < compilerArgs.Length; i++)
            argsPinned[i].Free(); // Free argument

        return result;
    }

    private static unsafe CompilationResult GetResult(NativeDxcCompileResult* resultPtr)
    {
        NativeDxcCompileError* errorPtr = DXCNative.DxcCompileResultGetError(resultPtr);

        byte[] objectBytes = [];
        string? compilationErrors = null;

        if (errorPtr != null)
        {
            char* errorStringPtr = DXCNative.DxcCompileErrorGetString(errorPtr);
            nuint errorStringLen = DXCNative.DxcCompileErrorGetStringLength(errorPtr);

            compilationErrors = Marshal.PtrToStringUni((IntPtr)errorStringPtr, (int)errorStringLen);
            DXCNative.DxcCompileErrorRelease(errorPtr);
        }
        else
        {
            NativeDxcCompileObject* objectPtr = DXCNative.DxcCompileResultGetObject(resultPtr);
            byte* objectBytesPtr = DXCNative.DxcCompileObjectGetBytes(objectPtr);
            nuint objectBytesLen = DXCNative.DxcCompileObjectGetBytesLength(objectPtr);

            objectBytes = new byte[(int)objectBytesLen];
            Marshal.Copy((IntPtr)objectBytesPtr, objectBytes, 0, (int)objectBytesLen);

            DXCNative.DxcCompileObjectRelease(objectPtr);
        }

        DXCNative.DxcCompileResultRelease(resultPtr);

        return new CompilationResult()
        {
            objectBytes = objectBytes,
            compilationErrors = compilationErrors
        };
    }
}