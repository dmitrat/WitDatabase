using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Database.AdoNet;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Infrastructure;

/// <summary>
/// Extension for configuring WitDatabase provider options in Entity Framework Core.
/// </summary>
public sealed class WitDbContextOptionsExtension : RelationalOptionsExtension
{
    #region Fields

    private bool m_inMemory;
    private WitDbParallelMode m_parallelMode = WitDbParallelMode.None;
    private int? m_maxWriters;
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
        : base(copyFrom)
    {
        m_inMemory = copyFrom.m_inMemory;
        m_parallelMode = copyFrom.m_parallelMode;
        m_maxWriters = copyFrom.m_maxWriters;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Creates a copy of this extension configured for in-memory mode.
    /// </summary>
    /// <param name="inMemory">True to enable in-memory mode.</param>
    /// <returns>A new extension instance.</returns>
    public WitDbContextOptionsExtension WithInMemory(bool inMemory = true)
    {
        var clone = (WitDbContextOptionsExtension)Clone();
        clone.m_inMemory = inMemory;
        return clone;
    }

    /// <summary>
    /// Creates a copy of this extension with the specified connection string.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>A new extension instance.</returns>
    public new WitDbContextOptionsExtension WithConnectionString(string connectionString)
    {
        return (WitDbContextOptionsExtension)base.WithConnectionString(connectionString);
    }

    /// <summary>
    /// Creates a copy of this extension with the specified parallel mode.
    /// </summary>
    /// <param name="parallelMode">The parallel mode.</param>
    /// <returns>A new extension instance.</returns>
    public WitDbContextOptionsExtension WithParallelMode(WitDbParallelMode parallelMode)
    {
        var clone = (WitDbContextOptionsExtension)Clone();
        clone.m_parallelMode = parallelMode;
        return clone;
    }

    /// <summary>
    /// Creates a copy of this extension with the specified max writers.
    /// </summary>
    /// <param name="maxWriters">The maximum number of parallel writers.</param>
    /// <returns>A new extension instance.</returns>
    public WitDbContextOptionsExtension WithMaxWriters(int maxWriters)
    {
        var clone = (WitDbContextOptionsExtension)Clone();
        clone.m_maxWriters = maxWriters;
        return clone;
    }

    /// <inheritdoc/>
    protected override RelationalOptionsExtension Clone()
    {
        return new WitDbContextOptionsExtension(this);
    }

    /// <summary>
    /// Gets the effective connection string with parallel mode options applied.
    /// </summary>
    public string? GetEffectiveConnectionString()
    {
        var connStr = ConnectionString;
        if (string.IsNullOrEmpty(connStr))
            return connStr;

        // Only modify if parallel mode is set
        if (m_parallelMode == WitDbParallelMode.None && !m_maxWriters.HasValue)
            return connStr;

        var builder = new WitDbConnectionStringBuilder(connStr);
        
        if (m_parallelMode != WitDbParallelMode.None)
            builder.ParallelMode = m_parallelMode;
        
        if (m_maxWriters.HasValue)
            builder.MaxWriters = m_maxWriters.Value;

        return builder.ConnectionString;
    }

    #endregion

    #region RelationalOptionsExtension

    /// <inheritdoc/>
    public override DbContextOptionsExtensionInfo Info => m_info ??= new ExtensionInfo(this);

    /// <inheritdoc/>
    public override void ApplyServices(IServiceCollection services)
    {
        services.AddEntityFrameworkWitDb();
    }

    /// <inheritdoc/>
    public override void Validate(IDbContextOptions options)
    {
        base.Validate(options);
        
        if (Connection == null && string.IsNullOrEmpty(ConnectionString) && !m_inMemory)
        {
            throw new InvalidOperationException(
                "A connection string or connection must be specified to use WitDatabase with Entity Framework Core.");
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets whether in-memory mode is enabled.
    /// </summary>
    public bool InMemory => m_inMemory;

    /// <summary>
    /// Gets the parallel mode.
    /// </summary>
    public WitDbParallelMode ParallelMode => m_parallelMode;

    /// <summary>
    /// Gets the maximum number of parallel writers.
    /// </summary>
    public int? MaxWriters => m_maxWriters;

    #endregion

    #region Nested Types

    private sealed class ExtensionInfo : RelationalExtensionInfo
    {
        #region Fields

        private const string REDACTED = "*****";

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
                hashCode.Add(base.GetServiceProviderHashCode());
                hashCode.Add(Extension.InMemory);
                hashCode.Add(Extension.ParallelMode);
                hashCode.Add(Extension.MaxWriters);
                m_serviceProviderHash = hashCode.ToHashCode();
            }

            return m_serviceProviderHash.Value;
        }

        /// <summary>
        /// Replaces the encryption password in a connection string with a placeholder.
        /// </summary>
        /// <remarks>
        /// EF Core writes <see cref="LogFragment"/> at Information level the first time a context is
        /// used, and surfaces <see cref="PopulateDebugInfo"/> in diagnostics, so neither may carry a
        /// secret. This <b>fails closed</b>: a connection string that cannot be parsed is replaced
        /// wholesale rather than logged as it stands, because an unparseable string is exactly the
        /// case where we cannot tell whether it holds a password.
        /// </remarks>
        internal static string? Redact(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return connectionString;

            try
            {
                var builder = new WitDbConnectionStringBuilder(connectionString);
                if (string.IsNullOrEmpty(builder.Password))
                    return connectionString;

                builder.Password = REDACTED;
                return builder.ToString();
            }
            catch
            {
                return "(connection string withheld - could not be parsed to redact credentials)";
            }
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is ExtensionInfo otherInfo
                && base.ShouldUseSameServiceProvider(other)
                && Extension.InMemory == otherInfo.Extension.InMemory
                && Extension.ParallelMode == otherInfo.Extension.ParallelMode
                && Extension.MaxWriters == otherInfo.Extension.MaxWriters;
        }

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["WitDb:ConnectionString"] = Redact(Extension.ConnectionString) ?? "(null)";
            debugInfo["WitDb:InMemory"] = Extension.InMemory.ToString();
            debugInfo["WitDb:ParallelMode"] = Extension.ParallelMode.ToString();
            if (Extension.MaxWriters.HasValue)
                debugInfo["WitDb:MaxWriters"] = Extension.MaxWriters.Value.ToString();
        }

        #endregion

        #region Properties

        private new WitDbContextOptionsExtension Extension => (WitDbContextOptionsExtension)base.Extension;

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
                        builder.Append('\'').Append(Redact(Extension.ConnectionString)).Append('\'');
                    }
                    else if (Extension.Connection != null)
                    {
                        builder.Append("(existing connection)");
                    }

                    if (Extension.ParallelMode != WitDbParallelMode.None)
                    {
                        builder.Append($" [Parallel: {Extension.ParallelMode}");
                        if (Extension.MaxWriters.HasValue)
                            builder.Append($", MaxWriters: {Extension.MaxWriters}");
                        builder.Append("]");
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
