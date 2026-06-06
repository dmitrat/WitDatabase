namespace OutWit.Database.Native;

/// <summary>
/// Managed implementation behind UCO exports (Marshal / heavy work stays out of UCO thunks).
/// </summary>
internal static class WitDbExportsCore
{
    public static unsafe WitDbStatusCode Open(
        byte* path,
        byte* password,
        bool createIfMissing,
        UIntPtr* outDb)
    {
        var pathStr = WitDbUtf8.PtrToString(path);
        if (string.IsNullOrWhiteSpace(pathStr))
        {
            return WitDbInterop.Fail(WitDbStatusCode.InvalidArgument, "path is required");
        }

        string? passwordStr = password == null ? null : WitDbUtf8.PtrToString(password);
        var status = WitDbInterop.Open(pathStr, passwordStr, createIfMissing, out var handle);
        *outDb = handle;
        return status;
    }
}
