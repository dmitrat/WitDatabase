using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Database.AdoNet;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Infrastructure;

/// <summary>
/// Extension for configuring WitDatabase provider options in Entity Framework Core.
/// </summary>
public sealed class WitDbContextOptionsExtension : IDbContextOptionsExtension
{
    #region Fields

    private string? m_connectionString;
    private WitDbConnection? m_connection;
    private bool m_inMemory;
    private DbContextOptionsExtensionInfo? m_info;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbContextOptionsExtension"/> class.
    /// </summary>
    public WitDbContextOptionsExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbContextOptionsExtension"/> class
    /// by copying from an existing instance.
    /// </summary>
    /// <param name="copyFrom">The instance to copy from.</param>
    private WitDbContextOptionsExtension(WitDbContextOptionsExtension copyFrom)
    {
        m_connectionString = copyFrom.m_connectionString;
        m_connection = copyFrom.m_connection;
        m_inMemory = copyFrom.m_inMemory;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Creates a copy of this extension with the specified connection string.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>A new extension instance.</returns>
    public WitDbContextOptionsExtension WithConnectionString(string connectionString)
    {
        var clone = Clone();
        clone.m_connectionString = connectionString;
        return clone;
    }

    /// <summary>
    /// Creates a copy of this extension with the specified connection.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <returns>A new extension instance.</returns>
    public WitDbContextOptionsExtension WithConnection(WitDbConnection connection)
    {
        var clone = Clone();
        clone.m_connection = connection;
        return clone;
    }

    /// <summary>
    /// Creates a copy of this extension configured for in-memory mode.
    /// </summary>
    /// <param name="inMemory">True to enable in-memory mode.</param>
    /// <returns>A new extension instance.</returns>
    public WitDbContextOptionsExtension WithInMemory(bool inMemory = true)
    {
        var clone = Clone();
        clone.m_inMemory = inMemory;
        return clone;
    }

    private WitDbContextOptionsExtension Clone()
    {
        return new WitDbContextOptionsExtension(this);
    }

    #endregion

    #region IDbContextOptionsExtension

    /// <inheritdoc/>
    public DbContextOptionsExtensionInfo Info => m_info ??= new ExtensionInfo(this);

    /// <inheritdoc/>
    public void ApplyServices(IServiceCollection services)
    {
        services.AddEntityFrameworkWitDb();
    }

    /// <inheritdoc/>
    public void Validate(IDbContextOptions options)
    {
        if (m_connection == null && string.IsNullOrEmpty(m_connectionString) && !m_inMemory)
        {
            throw new InvalidOperationException(
                "A connection string or connection must be specified to use WitDatabase with Entity Framework Core.");
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the connection string.
    /// </summary>
    public string? ConnectionString => m_connectionString;

    /// <summary>
    /// Gets the database connection.
    /// </summary>
    public WitDbConnection? Connection => m_connection;

    /// <summary>
    /// Gets whether in-memory mode is enabled.
    /// </summary>
    public bool InMemory => m_inMemory;

    #endregion

    #region Nested Types

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        #region Fields

        private string? m_logFragment;
        private int? m_serviceProviderHash;

        #endregion

        #region Constructors

        public ExtensionInfo(WitDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        #endregion

        #region Functions

        public override int GetServiceProviderHashCode()
        {
            if (m_serviceProviderHash == null)
            {
                var hashCode = new HashCode();
                hashCode.Add(Extension.ConnectionString);
                hashCode.Add(Extension.Connection);
                hashCode.Add(Extension.InMemory);
                m_serviceProviderHash = hashCode.ToHashCode();
            }

            return m_serviceProviderHash.Value;
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is ExtensionInfo otherInfo
                && Extension.ConnectionString == otherInfo.Extension.ConnectionString
                && Extension.Connection == otherInfo.Extension.Connection
                && Extension.InMemory == otherInfo.Extension.InMemory;
        }

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["WitDb:ConnectionString"] = Extension.ConnectionString ?? "(null)";
            debugInfo["WitDb:Connection"] = Extension.Connection != null ? "(set)" : "(null)";
            debugInfo["WitDb:InMemory"] = Extension.InMemory.ToString();
        }

        #endregion

        #region Properties

        private new WitDbContextOptionsExtension Extension => (WitDbContextOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => true;

        public override string LogFragment
        {
            get
            {
                if (m_logFragment == null)
                {
                    var builder = new System.Text.StringBuilder();
                    builder.Append("Using WitDatabase ");

                    if (Extension.InMemory)
                    {
                        builder.Append("(in-memory)");
                    }
                    else if (!string.IsNullOrEmpty(Extension.ConnectionString))
                    {
                        builder.Append("'").Append(Extension.ConnectionString).Append("'");
                    }
                    else if (Extension.Connection != null)
                    {
                        builder.Append("(existing connection)");
                    }

                    m_logFragment = builder.ToString();
                }

                return m_logFragment;
            }
        }

        #endregion
    }

    #endregion
}
