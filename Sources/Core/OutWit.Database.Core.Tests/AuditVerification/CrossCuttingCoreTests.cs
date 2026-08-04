using NUnit.Framework;
using OutWit.Database.Core.Builder;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Verification of the core-side <c>cross-cutting</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>See Docs/NEXT-SESSION-PLAN.md workstream B.</remarks>
[TestFixture]
[Category("AuditVerification")]
public class CrossCuttingCoreTests
{
    private static byte[] Key(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private static byte[] Value(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    #region Reopening an encrypted MVCC database

    /// <summary>
    /// FIXED in 12.2.0. Was: an encrypted database created with MVCC came back without it.
    /// </summary>
    /// <remarks>
    /// The marker's reasoning was right about the mechanism and wrong about the conclusion drawn from
    /// it. The encrypted overload cannot read the header <b>from the file</b> - it is inside the
    /// encrypted page - and that was taken to mean the configuration was unknowable, so
    /// <c>WithTransactions()</c> was called unconditionally. But the store decrypts the header as soon
    /// as it is built, and the transactional layer is built after the store: phase 12 reconciles the
    /// transaction model there, which is late enough to know and early enough to matter.
    /// </remarks>
    [Test]
    public void EncryptedMvccDatabaseStaysMvccWhenReopenedTest()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "encrypted.witdb");
        const string password = "correct horse battery staple";

        try
        {
            using (var created = new WitDatabaseBuilder()
                       .WithFilePath(path)
                       .WithBTree()
                       .WithEncryption(password)
                       .WithMvcc()
                       .Build())
            {
                Assert.That(created.SupportsMvcc, Is.True, "the database was built with MVCC");
                created.Put(Key("k"), Value("v"));
            }

            using var reopened = WitDatabase.Open(path, password);

            Assert.That(reopened.SupportsMvcc, Is.True,
                "reopening must preserve MVCC - the on-disk key format differs between the two");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>
    /// FIXED in 12.2.0. Was: the row written before the reopen came back NULL, because the two modes
    /// disagree about the on-disk key format and the reopen silently chose the other one.
    /// </summary>
    [Test]
    public void EncryptedMvccDatabaseStillReadsItsDataWhenReopenedTest()
    {
        // The consequence that would actually cost a user data: if the two modes disagree about the
        // on-disk key format, the rows written before the downgrade become unreachable.
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "encrypted.witdb");
        const string password = "correct horse battery staple";

        try
        {
            using (var created = new WitDatabaseBuilder()
                       .WithFilePath(path)
                       .WithBTree()
                       .WithEncryption(password)
                       .WithMvcc()
                       .Build())
            {
                created.Put(Key("k"), Value("v"));
            }

            using var reopened = WitDatabase.Open(path, password);

            Assert.That(reopened.Get(Key("k")), Is.EqualTo(Value("v")),
                "a row written before the reopen must still be readable after it");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    #endregion

    // CONFIRMED BY INSPECTION, consequence not reproduced - three findings in this dimension are
    // bare `catch { }` swallows whose effect needs injected I/O failure to observe. The swallow
    // itself is not in doubt; what is unproven is how often it fires and what it costs.
    //
    //  * "Disposal paths swallow write failures" (WitSqlEngine.cs:302). The final flush is wrapped
    //    in `try { m_database.Flush(); } catch { }` with the comment "Best effort - don't fail
    //    dispose on flush errors", so a failed last write is invisible to the caller. The second
    //    half of the claim - "skips cleanup on exception, leaking file handles" - does NOT hold for
    //    the flush: the catch guarantees m_database.Dispose() still runs. It holds for a different
    //    line: `m_currentTransaction?.Dispose()` sits *before* the try, unguarded, so a throwing
    //    transaction dispose skips the store dispose entirely. Right conclusion, wrong line.
    //
    //  * "LSM compaction swallows File.Delete failures" (StoreLsm.cs:521). Literally
    //    `try { File.Delete(file); } catch { }`, and SSTableReader opens with FileShare.Read
    //    (SSTableReader.cs:58), which on Windows refuses a delete while any reader holds the file -
    //    so the failure the swallow hides is the likely case, not the exotic one. Combined with the
    //    separate core-lsm finding that compaction has no manifest and infers the live set from the
    //    directory listing, an undeleted input file resurrects rows.
    //
    //  * "The IndexedDB/Blazor WASM story cannot work" (WitSqlEngine.Async.cs:1). The async engine
    //    file is confirmed **0 bytes** - `wc -c` says so - and WitDbConnection's async transaction
    //    methods are Task.Run wrappers around the synchronous ones, which is the "Task.Run-wrapped
    //    ADO.NET" half. Neither is in question; what is unverified is the README sample.

    #region Helpers

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "witdb-crosscut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
    }

    #endregion
}
