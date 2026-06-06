using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Native;

internal sealed class DbEntry : IDisposable
{
    public required WitDatabase Database { get; init; }
    public ITransaction? ActiveTransaction { get; set; }

    public void Dispose()
    {
        ActiveTransaction?.Dispose();
        Database.Dispose();
    }
}

internal static class WitDbHandleTable
{
    private static readonly Lock s_lock = new();
    private static readonly Dictionary<UIntPtr, DbEntry> s_databases = [];
    private static readonly Dictionary<UIntPtr, (DbEntry Owner, ITransaction Txn)> s_transactions = [];
    private static UIntPtr s_nextDb = new(1);
    private static UIntPtr s_nextTxn = new(0x8000_0000_0000_0001);

    public static UIntPtr AddDatabase(WitDatabase db)
    {
        lock (s_lock)
        {
            var handle = s_nextDb;
            s_nextDb = new UIntPtr(s_nextDb.ToUInt64() + 1);
            s_databases[handle] = new DbEntry { Database = db };
            return handle;
        }
    }

    public static bool TryGetDatabase(UIntPtr handle, out DbEntry? entry)
    {
        lock (s_lock)
        {
            if (s_databases.TryGetValue(handle, out var db))
            {
                entry = db;
                return true;
            }
        }

        entry = null;
        return false;
    }

    public static UIntPtr AddTransaction(DbEntry dbEntry, ITransaction txn)
    {
        lock (s_lock)
        {
            var handle = s_nextTxn;
            s_nextTxn = new UIntPtr(s_nextTxn.ToUInt64() + 1);
            s_transactions[handle] = (dbEntry, txn);
            dbEntry.ActiveTransaction = txn;
            return handle;
        }
    }

    public static bool TryGetTransaction(UIntPtr handle, out ITransaction? txn)
    {
        lock (s_lock)
        {
            if (s_transactions.TryGetValue(handle, out var pair))
            {
                txn = pair.Txn;
                return true;
            }
        }

        txn = null;
        return false;
    }

    public static bool RemoveTransaction(UIntPtr handle)
    {
        lock (s_lock)
        {
            if (!s_transactions.Remove(handle, out var pair))
            {
                return false;
            }

            pair.Owner.ActiveTransaction = null;
            pair.Txn.Dispose();
            return true;
        }
    }

    public static bool RemoveDatabase(UIntPtr handle)
    {
        lock (s_lock)
        {
            if (!s_databases.Remove(handle, out var entry))
            {
                return false;
            }

            entry.Dispose();
            return true;
        }
    }
}
