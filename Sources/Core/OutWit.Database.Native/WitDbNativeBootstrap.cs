using System.Runtime.CompilerServices;

namespace OutWit.Database.Native;

internal static class WitDbNativeBootstrap
{
    private static int s_initialized;

    [ModuleInitializer]
    internal static void OnModuleLoad()
    {
        WitDbNativeTrace.Write("module: load");
        WitDbClrThread.EnsureStarted();
        EnsureInitialized();
    }

    public static void EnsureInitialized()
    {
        if (Interlocked.CompareExchange(ref s_initialized, 1, 0) != 0)
        {
            return;
        }

        WitDbNativeTrace.Write("bootstrap: start");
        WitDbInterop.EnsureBootstrapped();
        WitDbNativeTrace.Write("bootstrap: done");
    }
}
