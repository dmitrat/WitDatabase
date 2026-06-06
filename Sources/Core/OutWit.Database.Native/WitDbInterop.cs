using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OutWit.Database.Core.BouncyCastle;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;

namespace OutWit.Database.Native;

internal static class WitDbInterop
{
    private static int s_bootstrapped;

    public static void EnsureBootstrapped()
    {
        if (Interlocked.Exchange(ref s_bootstrapped, 1) == 0)
        {
            BouncyCastleProviderRegistration.EnsureRegistered();
        }
    }

    public static WitDbStatusCode Open(
        string path,
        string? password,
        bool createIfMissing,
        out UIntPtr handle)
    {
        handle = UIntPtr.Zero;
        try
        {
            WitDbNativeTrace.Write($"interop: open path={path}");
            EnsureBootstrapped();
            WitDbNativeTrace.Write("interop: build");
            var db = BuildDatabase(path, password, createIfMissing);
            WitDbNativeTrace.Write("interop: built");
            handle = WitDbHandleTable.AddDatabase(db);
            WitDbLastError.Set(null);
            return WitDbStatusCode.Ok;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    /// <summary>
    /// Native ABI opens without cross-process file locks (Thread.Sleep in UCO is unsafe).
    /// Reopen/provider detection still uses Core <see cref="StorageDetector"/>.
    /// </summary>
    private static WitDatabase BuildDatabase(string path, string? password, bool createIfMissing)
    {
        var detection = StorageDetector.Detect(path);

        if (!detection.Exists)
        {
            if (!createIfMissing)
            {
                throw new FileNotFoundException("Database not found", path);
            }

            return password is null
                ? new WitDatabaseBuilder().WithFilePath(path).WithBTree().WithTransactions().WithoutFileLocking().Build()
                : new WitDatabaseBuilder().WithFilePath(path).WithBTree().WithEncryption(password).WithTransactions().WithoutFileLocking().Build();
        }

        if (password is null && detection.RequiresPassword)
        {
            throw new InvalidDataException(
                $"Database is encrypted with '{detection.EncryptionProvider}' provider. Password required.");
        }

        var builder = new WitDatabaseBuilder();

        if (detection.StoreType == "lsm" || detection.IsDirectory)
        {
            if (password is not null)
            {
                builder.WithLsmTree(path).WithEncryption(password);
            }
            else
            {
                builder.WithLsmTree(path);
            }
        }
        else
        {
            builder.WithFilePath(path).WithBTree();
            if (password is not null)
            {
                builder.WithEncryption(password);
            }
        }

        if (detection.HasTransactions)
        {
            if (detection.HasMvcc)
            {
                builder.WithMvcc();
            }
            else
            {
                builder.WithTransactions();
            }
        }
        else
        {
            builder.WithoutTransactions();
        }

        builder.WithoutFileLocking();
        return builder.Build();
    }

    public static WitDbStatusCode Close(UIntPtr handle)
    {
        if (handle == UIntPtr.Zero)
        {
            return Fail(WitDbStatusCode.InvalidHandle, "Invalid database handle");
        }

        if (!WitDbHandleTable.RemoveDatabase(handle))
        {
            return Fail(WitDbStatusCode.InvalidHandle, "Unknown database handle");
        }

        WitDbLastError.Set(null);
        return WitDbStatusCode.Ok;
    }

    public static WitDbStatusCode Get(UIntPtr dbHandle, ReadOnlySpan<byte> key, out byte[]? value)
    {
        value = null;
        if (!WitDbHandleTable.TryGetDatabase(dbHandle, out var entry) || entry is null)
        {
            return Fail(WitDbStatusCode.InvalidHandle, "Unknown database handle");
        }

        try
        {
            value = entry.Database.Get(key);
            WitDbLastError.Set(null);
            return WitDbStatusCode.Ok;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public static WitDbStatusCode Put(UIntPtr dbHandle, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (!WitDbHandleTable.TryGetDatabase(dbHandle, out var entry) || entry is null)
        {
            return Fail(WitDbStatusCode.InvalidHandle, "Unknown database handle");
        }

        try
        {
            entry.Database.Put(key, value);
            WitDbLastError.Set(null);
            return WitDbStatusCode.Ok;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public static WitDbStatusCode Delete(UIntPtr dbHandle, ReadOnlySpan<byte> key, out bool deleted)
    {
        deleted = false;
        if (!WitDbHandleTable.TryGetDatabase(dbHandle, out var entry) || entry is null)
        {
            return Fail(WitDbStatusCode.InvalidHandle, "Unknown database handle");
        }

        try
        {
            deleted = entry.Database.Delete(key);
            WitDbLastError.Set(null);
            return WitDbStatusCode.Ok;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public static WitDbStatusCode TxnBegin(UIntPtr dbHandle, out UIntPtr txnHandle)
    {
        txnHandle = UIntPtr.Zero;
        if (!WitDbHandleTable.TryGetDatabase(dbHandle, out var entry) || entry is null)
        {
            return Fail(WitDbStatusCode.InvalidHandle, "Unknown database handle");
        }

        if (entry.ActiveTransaction is not null)
        {
            return Fail(WitDbStatusCode.TxnActive, "Transaction already active on this database handle");
        }

        if (!entry.Database.SupportsTransactions)
        {
            return Fail(WitDbStatusCode.TxnNotSupported, "Transactions are not enabled for this store");
        }

        try
        {
            var txn = entry.Database.BeginTransaction();
            txnHandle = WitDbHandleTable.AddTransaction(entry, txn);
            WitDbLastError.Set(null);
            return WitDbStatusCode.Ok;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public static WitDbStatusCode TxnCommit(UIntPtr txnHandle)
    {
        if (!WitDbHandleTable.TryGetTransaction(txnHandle, out var txn) || txn is null)
        {
            return Fail(WitDbStatusCode.InvalidHandle, "Unknown transaction handle");
        }

        try
        {
            txn.Commit();
            WitDbHandleTable.RemoveTransaction(txnHandle);
            WitDbLastError.Set(null);
            return WitDbStatusCode.Ok;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public static WitDbStatusCode TxnRollback(UIntPtr txnHandle)
    {
        if (!WitDbHandleTable.TryGetTransaction(txnHandle, out var txn) || txn is null)
        {
            return Fail(WitDbStatusCode.InvalidHandle, "Unknown transaction handle");
        }

        try
        {
            txn.Rollback();
            WitDbHandleTable.RemoveTransaction(txnHandle);
            WitDbLastError.Set(null);
            return WitDbStatusCode.Ok;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public static WitDbStatusCode TxnGet(UIntPtr txnHandle, ReadOnlySpan<byte> key, out byte[]? value)
    {
        value = null;
        if (!WitDbHandleTable.TryGetTransaction(txnHandle, out var txn) || txn is null)
        {
            return Fail(WitDbStatusCode.InvalidHandle, "Unknown transaction handle");
        }

        try
        {
            value = txn.Get(key);
            WitDbLastError.Set(null);
            return WitDbStatusCode.Ok;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public static WitDbStatusCode TxnPut(UIntPtr txnHandle, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (!WitDbHandleTable.TryGetTransaction(txnHandle, out var txn) || txn is null)
        {
            return Fail(WitDbStatusCode.InvalidHandle, "Unknown transaction handle");
        }

        try
        {
            txn.Put(key, value);
            WitDbLastError.Set(null);
            return WitDbStatusCode.Ok;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public static WitDbStatusCode TxnDelete(UIntPtr txnHandle, ReadOnlySpan<byte> key, out bool deleted)
    {
        deleted = false;
        if (!WitDbHandleTable.TryGetTransaction(txnHandle, out var txn) || txn is null)
        {
            return Fail(WitDbStatusCode.InvalidHandle, "Unknown transaction handle");
        }

        try
        {
            deleted = txn.Delete(key);
            WitDbLastError.Set(null);
            return WitDbStatusCode.Ok;
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public static unsafe byte* AllocCopy(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
        {
            return null;
        }

        var ptr = (byte*)NativeMemory.Alloc((nuint)source.Length);
        source.CopyTo(new Span<byte>(ptr, source.Length));
        return ptr;
    }

    internal static WitDbStatusCode Fail(WitDbStatusCode code, string message)
    {
        WitDbLastError.Set(message);
        return code;
    }

    internal static WitDbStatusCode MapException(Exception ex)
    {
        WitDbLastError.Set(ex.Message);
        return ex switch
        {
            FileNotFoundException => WitDbStatusCode.NotFound,
            InvalidDataException => WitDbStatusCode.PasswordRequired,
            ConfigurationMismatchException => WitDbStatusCode.ConfigMismatch,
            ProviderNotFoundException => WitDbStatusCode.UnknownProvider,
            CryptographicException => WitDbStatusCode.WrongPassword,
            ArgumentException => WitDbStatusCode.InvalidArgument,
            _ => WitDbStatusCode.StoreError,
        };
    }
}
