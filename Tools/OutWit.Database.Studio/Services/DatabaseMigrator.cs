using OutWit.Database.AdoNet;
using OutWit.Database.Core.Builder;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Encrypting a database, and removing its encryption, are a new database and a move into it (WS-58).
/// </summary>
/// <remarks>
/// <para>
/// <b>Replacing a password no longer comes here, and this paragraph is why it took a release to
/// notice.</b> It used to open with "there is nothing to change in place - the encryption key is
/// derived from the password", which was true when it was written and stopped being true at the
/// format change: the data key is now drawn at random and the password only WRAPS it, so replacing a
/// password rewrites 60 bytes and no page. The engine grew <c>CryptoHeader.Rewrap</c> for exactly
/// that and nothing called it, and anyone asking "why does Studio copy the whole database?" read
/// this paragraph and stopped. A comment records what was true when somebody last looked; this one
/// went on answering with confidence after its subject had changed underneath it.
/// </para>
/// <para>
/// <b>What still belongs here.</b> Going from no encryption to a password, and from a password to
/// none, both mean every page is rewritten - there is no wrapped key to make in the first case and
/// no way to leave ciphertext readable in the second. Those two are migrations and always will be,
/// and the original is left exactly as it was, which is what makes them safe to try.
/// </para>
/// <para>
/// <b>Encrypting an unencrypted database, and removing the encryption, are the same operation.</b>
/// Only the target's settings differ, which is why this takes a whole <see cref="ConnectionInfo"/> for
/// the target rather than a password.
/// </para>
/// <para>
/// <b>The transfer is a SCRIPT, so it has to be verified.</b> A byte copy is the same bytes by
/// construction; a dump is statements that are executed again, and the person who started it is the
/// one who has to be shown that nothing was lost. The verification counts every table on both sides by
/// scanning - not with <c>COUNT(*)</c>, which on this engine is a number kept beside the rows and can
/// agree with itself while they are gone.
/// </para>
/// </remarks>
public static class DatabaseMigrator
{
    #region Functions

    /// <summary>
    /// Builds <paramref name="target"/> and moves the source's schema and data into it.
    /// </summary>
    public static async Task<MigrationResult> MigrateAsync(
        IDatabaseSession source,
        ConnectionInfo target,
        IProgress<MigrationStep>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (string.IsNullOrWhiteSpace(target.FilePath))
            return Failed(target, "The new database has no path.");

        if (File.Exists(target.FilePath) || Directory.Exists(target.FilePath))
            return Failed(target, "There is already something at that path.");

        try
        {
            progress?.Report(new MigrationStep("Password.Step.Creating", target.EncryptionProvider));

            Create(target);

            progress?.Report(new MigrationStep("Password.Step.Reading"));

            // The whole database as a script, in dependency order - the same writer the dump uses, so
            // a shape it cannot render is named there rather than lost here.
            var script = await DatabaseDump.WriteAsync(source, new DumpOptions(), ct);

            progress?.Report(new MigrationStep("Password.Step.Writing"));

            await using (var connection = new WitDbConnection(target.BuildConnectionString()))
            {
                await connection.OpenAsync(ct);

                await RunAsync(connection, script, ct);
            }

            progress?.Report(new MigrationStep("Password.Step.Verifying"));

            var verification = await VerifyAsync(source, target, ct);

            return new MigrationResult(
                verification.Any(check => !check.Matches)
                    ? MigrationOutcome.RowsDoNotMatch
                    : MigrationOutcome.Transferred,
                target.FilePath,
                verification);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(target, ex.Message);
        }
    }

    #endregion

    #region Tools

    /// <summary>
    /// Creates the target with the settings it was described with, then closes it again so that the
    /// transfer opens it in the ordinary way.
    /// </summary>
    private static void Create(ConnectionInfo target)
    {
        var builder = new WitDatabaseBuilder().WithFilePath(target.FilePath);

        if (target.StorageEngine == "lsm")
            builder.WithLsmTree(target.FilePath);
        else
            builder.WithBTree();

        if (target.IsEncrypted && !string.IsNullOrEmpty(target.Password))
            builder.WithEncryption(target.Password);

        builder.WithMvcc();

        using var database = builder.Build();
    }

    /// <summary>
    /// Runs the script one statement at a time, so that a refusal names the statement it refused.
    /// </summary>
    private static async Task RunAsync(WitDbConnection connection, string script, CancellationToken ct)
    {
        var split = SqlScript.Split(script);

        if (!split.IsSuccess)
        {
            throw new InvalidOperationException(
                string.Join("; ", split.Errors.Select(error => error.Message)));
        }

        for (var i = 0; i < split.Statements.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var statement = split.Statements[i];

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement.Text;

                await command.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                // The statement, not just the message. A transfer of a hundred statements that fails
                // with "Column 'Id' not found" and nothing else is a report nobody can act on - which
                // is exactly what this said until the first migration was run.
                throw new InvalidOperationException(
                    $"Statement {i + 1} of {split.Statements.Count} failed: {ex.Message}"
                    + Environment.NewLine + SqlScript.Shorten(statement.Text), ex);
            }
        }
    }

    /// <summary>
    /// Counts every table on both sides, by reading the rows.
    /// </summary>
    private static async Task<IReadOnlyList<TableRowCheck>> VerifyAsync(IDatabaseSession source,
        ConnectionInfo target, CancellationToken ct)
    {
        var checks = new List<TableRowCheck>();

        var tables = await source.GetTablesAsync(ct);

        await using var connection = new WitDbConnection(target.BuildConnectionString());
        await connection.OpenAsync(ct);

        foreach (var table in tables)
        {
            ct.ThrowIfCancellationRequested();

            var inSource = await source.ScanAsync($"SELECT * FROM [{table.Name}]", ct);
            var inTarget = await ScanAsync(connection, $"SELECT * FROM [{table.Name}]", ct);

            checks.Add(new TableRowCheck(table.Name, inSource, inTarget));
        }

        return checks;
    }

    private static async Task<long> ScanAsync(WitDbConnection connection, string sql, CancellationToken ct)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync(ct);

            var rows = 0L;

            while (await reader.ReadAsync(ct))
                rows++;

            return rows;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A table that is not there at all counts as none of its rows, which is what the
            // verification is for.
            return -1;
        }
    }

    private static MigrationResult Failed(ConnectionInfo target, string message) =>
        new(MigrationOutcome.Failed, target.FilePath ?? string.Empty, [], message);

    #endregion
}
