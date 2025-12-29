using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OutWit.Database.EntityFramework.Infrastructure;

namespace OutWit.Database.EntityFramework;

/// <summary>
/// The primary database provider service for WitDatabase.
/// </summary>
public sealed class WitDatabaseProvider : IDatabaseProvider
{
    #region Constants

    /// <summary>
    /// The provider invariant name.
    /// </summary>
    public const string PROVIDER_NAME = "OutWit.Database.EntityFramework";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDatabaseProvider"/> class.
    /// </summary>
    public WitDatabaseProvider()
    {
    }

    #endregion

    #region IDatabaseProvider

    /// <inheritdoc/>
    public string Name => PROVIDER_NAME;

    /// <inheritdoc/>
    public bool IsConfigured(IDbContextOptions options)
    {
        return options.FindExtension<WitDbContextOptionsExtension>() != null;
    }

    #endregion
}
