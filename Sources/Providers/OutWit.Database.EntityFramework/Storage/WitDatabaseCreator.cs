using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OutWit.Database.AdoNet;

namespace OutWit.Database.EntityFramework.Storage;

/// <summary>
/// Creates and manages WitDatabase database instances.
/// </summary>
public sealed class WitDatabaseCreator : RelationalDatabaseCreator
{
    #region Fields

    private readonly IRelationalConnection m_connection;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDatabaseCreator"/> class.
    /// </summary>
    /// <param name="dependencies">The database creator dependencies.</param>
    public WitDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies)
        : base(dependencies)
    {
        m_connection = dependencies.Connection;
    }

    #endregion

    #region Database Existence

    /// <inheritdoc/>
    public override bool Exists()
    {
        try
        {
            // For in-memory databases, always return true if we can open
            if (IsInMemory())
            {
                return true;
            }

            // For file-based databases, check if the file exists
            var dataSource = GetDataSource();
            if (string.IsNullOrEmpty(dataSource))
            {
                return false;
            }

            return File.Exists(dataSource);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public override async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(Exists, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override bool HasTables()
    {
        try
        {
            m_connection.Open();

            using var command = m_connection.DbConnection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";

            var result = command.ExecuteScalar();
            return result != null && Convert.ToInt64(result) > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            m_connection.Close();
        }
    }

    /// <inheritdoc/>
    public override async Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await m_connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = m_connection.DbConnection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result != null && Convert.ToInt64(result) > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            await m_connection.CloseAsync().ConfigureAwait(false);
        }
    }

    #endregion

    #region Create/Delete

    /// <inheritdoc/>
    public override void Create()
    {
        // For WitDatabase, opening a connection to a non-existent file creates it
        m_connection.Open();
        m_connection.Close();
    }

    /// <inheritdoc/>
    public override async Task CreateAsync(CancellationToken cancellationToken = default)
    {
        await m_connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await m_connection.CloseAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override void Delete()
    {
        // Close any open connections first
        m_connection.Close();

        if (IsInMemory())
        {
            return;
        }

        var dataSource = GetDataSource();
        if (!string.IsNullOrEmpty(dataSource) && File.Exists(dataSource))
        {
            File.Delete(dataSource);
        }
    }

    /// <inheritdoc/>
    public override async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        await m_connection.CloseAsync().ConfigureAwait(false);

        if (IsInMemory())
        {
            return;
        }

        var dataSource = GetDataSource();
        if (!string.IsNullOrEmpty(dataSource) && File.Exists(dataSource))
        {
            await Task.Run(() => File.Delete(dataSource), cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region Helpers

    private bool IsInMemory()
    {
        var connectionString = m_connection.ConnectionString;
        if (string.IsNullOrEmpty(connectionString))
        {
            return false;
        }

        var builder = new WitDbConnectionStringBuilder(connectionString);
        return builder.Mode == WitDbConnectionMode.Memory ||
               string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);
    }

    private string? GetDataSource()
    {
        var connectionString = m_connection.ConnectionString;
        if (string.IsNullOrEmpty(connectionString))
        {
            return null;
        }

        var builder = new WitDbConnectionStringBuilder(connectionString);
        return builder.DataSource;
    }

    #endregion
}
