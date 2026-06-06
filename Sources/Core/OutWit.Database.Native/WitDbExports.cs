using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OutWit.Database.Native;

public static class WitDbExports
{
    [UnmanagedCallersOnly(EntryPoint = "witdb_abi_version", CallConvs = [typeof(CallConvCdecl)])]
    public static uint AbiVersion() => 1;

    [UnmanagedCallersOnly(EntryPoint = "witdb_last_error_message", CallConvs = [typeof(CallConvCdecl)])]
    public static IntPtr LastErrorMessage() => WitDbLastError.GetUtf8Pointer();

    [UnmanagedCallersOnly(EntryPoint = "witdb_open", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe uint Open(IntPtr path, IntPtr password, byte createIfMissing, UIntPtr* outDb)
    {
        if (outDb == null)
        {
            return (uint)WitDbStatusCode.InvalidArgument;
        }

        if (path == IntPtr.Zero)
        {
            return (uint)WitDbInterop.Fail(WitDbStatusCode.InvalidArgument, "path is required");
        }

        try
        {
            WitDbNativeTrace.Write("uco: witdb_open");
            return (uint)WitDbClrThread.Run(() =>
            {
                WitDbNativeBootstrap.EnsureInitialized();
                return WitDbExportsCore.Open(
                    (byte*)path,
                    password == IntPtr.Zero ? null : (byte*)password,
                    createIfMissing != 0,
                    outDb);
            });
        }
        catch (Exception ex)
        {
            return (uint)WitDbInterop.MapException(ex);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "witdb_close", CallConvs = [typeof(CallConvCdecl)])]
    public static uint Close(UIntPtr db) => (uint)WitDbInterop.Close(db);

    [UnmanagedCallersOnly(EntryPoint = "witdb_get", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe uint Get(
        UIntPtr db,
        byte* key,
        uint keyLen,
        byte** outValue,
        uint* outValueLen)
    {
        if (key == null || keyLen == 0 || outValue == null || outValueLen == null)
        {
            return (uint)WitDbInterop.Fail(WitDbStatusCode.InvalidArgument, "invalid get arguments");
        }

        var status = WitDbInterop.Get(db, new ReadOnlySpan<byte>(key, (int)keyLen), out var value);
        if (status != WitDbStatusCode.Ok)
        {
            return (uint)status;
        }

        if (value is null)
        {
            *outValue = null;
            *outValueLen = 0;
            return (uint)WitDbStatusCode.Ok;
        }

        *outValue = WitDbInterop.AllocCopy(value);
        *outValueLen = (uint)value.Length;
        return (uint)WitDbStatusCode.Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "witdb_put", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe uint Put(
        UIntPtr db,
        byte* key,
        uint keyLen,
        byte* value,
        uint valueLen)
    {
        if (key == null || keyLen == 0 || value == null)
        {
            return (uint)WitDbInterop.Fail(WitDbStatusCode.InvalidArgument, "invalid put arguments");
        }

        return (uint)WitDbInterop.Put(
            db,
            new ReadOnlySpan<byte>(key, (int)keyLen),
            new ReadOnlySpan<byte>(value, (int)valueLen));
    }

    [UnmanagedCallersOnly(EntryPoint = "witdb_delete", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe uint Delete(UIntPtr db, byte* key, uint keyLen, byte* outDeleted)
    {
        if (key == null || keyLen == 0 || outDeleted == null)
        {
            return (uint)WitDbInterop.Fail(WitDbStatusCode.InvalidArgument, "invalid delete arguments");
        }

        var status = WitDbInterop.Delete(db, new ReadOnlySpan<byte>(key, (int)keyLen), out var deleted);
        if (status == WitDbStatusCode.Ok)
        {
            *outDeleted = deleted ? (byte)1 : (byte)0;
        }

        return (uint)status;
    }

    [UnmanagedCallersOnly(EntryPoint = "witdb_txn_begin", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe uint TxnBegin(UIntPtr db, UIntPtr* outTxn)
    {
        if (outTxn == null)
        {
            return (uint)WitDbStatusCode.InvalidArgument;
        }

        var status = WitDbInterop.TxnBegin(db, out var txn);
        *outTxn = txn;
        return (uint)status;
    }

    [UnmanagedCallersOnly(EntryPoint = "witdb_txn_commit", CallConvs = [typeof(CallConvCdecl)])]
    public static uint TxnCommit(UIntPtr txn) => (uint)WitDbInterop.TxnCommit(txn);

    [UnmanagedCallersOnly(EntryPoint = "witdb_txn_rollback", CallConvs = [typeof(CallConvCdecl)])]
    public static uint TxnRollback(UIntPtr txn) => (uint)WitDbInterop.TxnRollback(txn);

    [UnmanagedCallersOnly(EntryPoint = "witdb_txn_get", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe uint TxnGet(
        UIntPtr txn,
        byte* key,
        uint keyLen,
        byte** outValue,
        uint* outValueLen)
    {
        if (key == null || keyLen == 0 || outValue == null || outValueLen == null)
        {
            return (uint)WitDbInterop.Fail(WitDbStatusCode.InvalidArgument, "invalid txn_get arguments");
        }

        var status = WitDbInterop.TxnGet(txn, new ReadOnlySpan<byte>(key, (int)keyLen), out var value);
        if (status != WitDbStatusCode.Ok)
        {
            return (uint)status;
        }

        if (value is null)
        {
            *outValue = null;
            *outValueLen = 0;
            return (uint)WitDbStatusCode.Ok;
        }

        *outValue = WitDbInterop.AllocCopy(value);
        *outValueLen = (uint)value.Length;
        return (uint)WitDbStatusCode.Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "witdb_txn_put", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe uint TxnPut(
        UIntPtr txn,
        byte* key,
        uint keyLen,
        byte* value,
        uint valueLen)
    {
        if (key == null || keyLen == 0 || value == null)
        {
            return (uint)WitDbInterop.Fail(WitDbStatusCode.InvalidArgument, "invalid txn_put arguments");
        }

        return (uint)WitDbInterop.TxnPut(
            txn,
            new ReadOnlySpan<byte>(key, (int)keyLen),
            new ReadOnlySpan<byte>(value, (int)valueLen));
    }

    [UnmanagedCallersOnly(EntryPoint = "witdb_txn_delete", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe uint TxnDelete(UIntPtr txn, byte* key, uint keyLen, byte* outDeleted)
    {
        if (key == null || keyLen == 0 || outDeleted == null)
        {
            return (uint)WitDbInterop.Fail(WitDbStatusCode.InvalidArgument, "invalid txn_delete arguments");
        }

        var status = WitDbInterop.TxnDelete(txn, new ReadOnlySpan<byte>(key, (int)keyLen), out var deleted);
        if (status == WitDbStatusCode.Ok)
        {
            *outDeleted = deleted ? (byte)1 : (byte)0;
        }

        return (uint)status;
    }

    [UnmanagedCallersOnly(EntryPoint = "witdb_buffer_free", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe void BufferFree(byte* ptr)
    {
        if (ptr != null)
        {
            NativeMemory.Free(ptr);
        }
    }
}
