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

    /// <inheritdoc/>
    protected override RelationalOptionsExtension Clone()
    {
        return new WitDbContextOptionsExtension(this);
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

    #endregion

    #region Nested Types

    private sealed class ExtensionInfo : RelationalExtensionInfo
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
                hashCode.Add(base.GetServiceProviderHashCode());
                hashCode.Add(Extension.InMemory);
                m_serviceProviderHash = hashCode.ToHashCode();
            }

            return m_serviceProviderHash.Value;
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is ExtensionInfo otherInfo
                && base.ShouldUseSameServiceProvider(other)
                && Extension.InMemory == otherInfo.Extension.InMemory;
        }

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["WitDb:ConnectionString"] = Extension.ConnectionString ?? "(null)";
            debugInfo["WitDb:InMemory"] = Extension.InMemory.ToString();
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
                        builder.Append("'").Append(Extension.ConnectionString).Append("'}");
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
