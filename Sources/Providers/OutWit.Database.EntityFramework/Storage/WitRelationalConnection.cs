using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OutWit.Database.AdoNet;
using OutWit.Database.EntityFramework.Infrastructure;

namespace OutWit.Database.EntityFramework.Storage;

/// <summary>
/// Represents a relational connection to a WitDatabase database.
/// </summary>
public sealed class WitRelationalConnection : RelationalConnection
{
    #region Fields

    private readonly string? m_connectionString;
    private readonly WitDbConnection? m_existingConnection;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitRelationalConnection"/> class.
    /// </summary>
    /// <param name="dependencies">The relational connection dependencies.</param>
    public WitRelationalConnection(RelationalConnectionDependencies dependencies)
        : base(dependencies)
    {
        var extension = dependencies.ContextOptions.FindExtension<WitDbContextOptionsExtension>();
        
        if (extension != null)
        {
            m_connectionString = extension.ConnectionString;
            m_existingConnection = extension.Connection as WitDbConnection;
        }
    }

    #endregion

    #region Connection Creation

    /// <inheritdoc/>
    protected override DbConnection CreateDbConnection()
    {
        if (m_existingConnection != null)
        {
            return m_existingConnection;
        }

        var connectionString = m_connectionString ?? ConnectionString;
        
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "A connection string must be specified to create a WitDatabase connection.");
        }

        return new WitDbConnection(connectionString);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the connection string for this connection.
    /// </summary>
    public new string? ConnectionString => m_connectionString ?? base.ConnectionString;

    #endregion
}
