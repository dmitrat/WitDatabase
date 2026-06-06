using System.Runtime.InteropServices;
using System.Text;

namespace OutWit.Database.Native;

internal static class WitDbLastError
{
    private const int MaxBytes = 4096;

    [ThreadStatic]
    private static string? t_message;

    [ThreadStatic]
    private static byte[]? t_buffer;

    [ThreadStatic]
    private static GCHandle t_pin;

    public static void Set(string? message) => t_message = message;

    public static string? GetMessage() => t_message;

    public static IntPtr GetUtf8Pointer()
    {
        var msg = t_message ?? string.Empty;
        t_buffer ??= new byte[MaxBytes];
        if (!t_pin.IsAllocated)
        {
            t_pin = GCHandle.Alloc(t_buffer, GCHandleType.Pinned);
        }

        var len = Encoding.UTF8.GetBytes(msg, 0, msg.Length, t_buffer, 0);
        if (len >= MaxBytes)
        {
            len = MaxBytes - 1;
        }

        t_buffer[len] = 0;
        return t_pin.AddrOfPinnedObject();
    }
}
