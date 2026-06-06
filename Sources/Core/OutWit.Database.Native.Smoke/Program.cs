using System.Runtime.InteropServices;
using OutWit.Database.Native;

var path = Path.Combine(Path.GetTempPath(), $"witdb-smoke-{Guid.NewGuid():N}.witdb");
var mode = args.FirstOrDefault() ?? "pinvoke";

if (mode == "managed")
{
    Console.WriteLine($"[managed] Opening {path}");
    var status = WitDbInterop.Open(path, null, createIfMissing: true, out var handle);
    Console.WriteLine($"open={status} handle={handle}");
    if (status != WitDbStatusCode.Ok)
    {
        Console.WriteLine(WitDbLastError.GetMessage());
        return 1;
    }

    WitDbInterop.Close(handle);
    Console.WriteLine("ok");
    return 0;
}

var publishDll = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..",
    "OutWit.Database.Native",
    "bin", "Release", "net10.0", "win-x64", "publish", "witdb.dll"));
if (!File.Exists(publishDll))
{
    publishDll = Environment.GetEnvironmentVariable("WITDB_NATIVE_PATH") ?? publishDll;
}

Console.WriteLine($"[pinvoke] dll={publishDll}");
NativeLibrary.SetDllImportResolver(
    typeof(WitDbNative).Assembly,
    (_, _, _) => NativeLibrary.Load(publishDll));
Console.WriteLine($"[pinvoke] Opening {path}");
var code = WitDbNative.witdb_open(path, null, 1, out var pinvokeHandle);
Console.WriteLine($"open={code} handle={pinvokeHandle}");
if (code != 0)
{
    var msg = WitDbNative.witdb_last_error_message();
    Console.WriteLine(Marshal.PtrToStringUTF8(msg));
    return 1;
}

WitDbNative.witdb_close(pinvokeHandle);
Console.WriteLine("ok");
return 0;

internal static partial class WitDbNative
{
    [LibraryImport("witdb", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial uint witdb_open(
        string path,
        string? password,
        byte create_if_missing,
        out UIntPtr out_db);

    [LibraryImport("witdb")]
    internal static partial uint witdb_close(UIntPtr db);

    [LibraryImport("witdb", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr witdb_last_error_message();
}
