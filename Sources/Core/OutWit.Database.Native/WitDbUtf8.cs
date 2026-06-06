using System.Text;

namespace OutWit.Database.Native;

internal static class WitDbUtf8
{
    /// <summary>
    /// Decode null-terminated UTF-8 from native pointer without Marshal (safe from UCO entry).
    /// </summary>
    public static unsafe string? PtrToString(byte* ptr)
    {
        if (ptr == null)
        {
            return null;
        }

        var length = 0;
        while (ptr[length] != 0)
        {
            length++;
        }

        if (length == 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(ptr, length);
    }
}
