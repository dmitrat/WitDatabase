using System.Data;
using System.Data.Common;
using System.Text;
using OutWit.Database.Types;

namespace OutWit.Database.AdoNet;

/// <summary>
/// Automatically generates single-table commands used to reconcile changes made to a DataSet
/// with a WitDatabase database.
/// </summary>
public sealed class WitDbCommandBuilder : DbCommandBuilder
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbCommandBuilder"/> class.
    /// </summary>
    public WitDbCommandBuilder()
    {
        QuotePrefix = "\"";
        QuoteSuffix = "\"";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbCommandBuilder"/> class
    /// with the specified data adapter.
    /// </summary>
    /// <param name="adapter">The data adapter.</param>
    public WitDbCommandBuilder(WitDbDataAdapter adapter)
        : this()
    {
        DataAdapter = adapter;
    }

    #endregion

    #region DbCommandBuilder Implementation

    /// <inheritdoc/>
    /// <summary>
    /// Wraps an identifier in this builder's quote characters, doubling any that appear inside it.
    /// </summary>
    /// <remarks>
    /// Overridden because the base implementation <b>throws</b>, and did so even though this builder's
    /// constructor has always set <c>QuotePrefix</c> and <c>QuoteSuffix</c> to <c>"</c> - it knew the
    /// answer and had no way to give it. Found by the phase 6 contract census, in its "inherited"
    /// column, which exists for exactly this: a base implementation that is wrong for this provider.
    /// </remarks>
    public override string QuoteIdentifier(string unquotedIdentifier)
    {
        ArgumentNullException.ThrowIfNull(unquotedIdentifier);

        if (string.IsNullOrEmpty(QuotePrefix) || string.IsNullOrEmpty(QuoteSuffix))
            return unquotedIdentifier;

        return QuotePrefix + unquotedIdentifier.Replace(QuoteSuffix, QuoteSuffix + QuoteSuffix) + QuoteSuffix;
    }

    /// <summary>
    /// Takes this builder's quote characters back off, undoubling any that were escaped inside.
    /// </summary>
    /// <remarks>
    /// An identifier that is not quoted is returned as it came, which is what a caller unquoting
    /// whatever a schema handed them needs.
    /// </remarks>
    public override string UnquoteIdentifier(string quotedIdentifier)
    {
        ArgumentNullException.ThrowIfNull(quotedIdentifier);

        if (string.IsNullOrEmpty(QuotePrefix) || string.IsNullOrEmpty(QuoteSuffix))
            return quotedIdentifier;

        if (!quotedIdentifier.StartsWith(QuotePrefix, StringComparison.Ordinal) ||
            !quotedIdentifier.EndsWith(QuoteSuffix, StringComparison.Ordinal) ||
            quotedIdentifier.Length < QuotePrefix.Length + QuoteSuffix.Length)
        {
            return quotedIdentifier;
        }

        var inner = quotedIdentifier.Substring(
            QuotePrefix.Length,
            quotedIdentifier.Length - QuotePrefix.Length - QuoteSuffix.Length);

        return inner.Replace(QuoteSuffix + QuoteSuffix, QuoteSuffix);
    }

    protected override void ApplyParameterInfo(DbParameter parameter, DataRow row, StatementType statementType, bool whereClause)
    {
        var witParam = (WitDbParameter)parameter;
        
        // Get type info from schema
        if (row.Table.Columns.Contains("ProviderType"))
        {
            var providerType = row["ProviderType"];
            if (providerType != DBNull.Value)
            {
                witParam.DbType = MapProviderTypeToDbType((int)providerType);
            }
        }
    }

    /// <inheritdoc/>
    protected override string GetParameterName(int parameterOrdinal)
    {
        return $"@p{parameterOrdinal}";
    }

    /// <inheritdoc/>
    protected override string GetParameterName(string parameterName)
    {
        return $"@{parameterName}";
    }

    /// <inheritdoc/>
    protected override string GetParameterPlaceholder(int parameterOrdinal)
    {
        return GetParameterName(parameterOrdinal);
    }

    /// <inheritdoc/>
    protected override void SetRowUpdatingHandler(DbDataAdapter adapter)
    {
        if (adapter is WitDbDataAdapter witAdapter)
        {
            witAdapter.RowUpdating += OnRowUpdating;
        }
    }

    private void OnRowUpdating(object? sender, RowUpdatingEventArgs e)
    {
        RowUpdatingHandler(e);
    }

    private static DbType MapProviderTypeToDbType(int providerType)
    {
        // Map WitSqlType to DbType
        return (WitSqlType)providerType switch
        {
            WitSqlType.Null => DbType.Object,
            WitSqlType.Integer => DbType.Int64,
            WitSqlType.Real => DbType.Double,
            WitSqlType.Text => DbType.String,
            WitSqlType.Blob => DbType.Binary,
            WitSqlType.Boolean => DbType.Boolean,
            WitSqlType.Decimal => DbType.Decimal,
            WitSqlType.DateTime => DbType.DateTime,
            WitSqlType.DateOnly => DbType.Date,
            WitSqlType.TimeOnly => DbType.Time,
            WitSqlType.TimeSpan => DbType.Time,
            WitSqlType.Guid => DbType.Guid,
            WitSqlType.DateTimeOffset => DbType.DateTimeOffset,
            WitSqlType.Json => DbType.String,
            WitSqlType.RowVersion => DbType.Binary,
            _ => DbType.Object
        };
    }

    #endregion

    #region Command Generation

    /// <summary>
    /// Gets the automatically generated INSERT command.
    /// </summary>
    /// <returns>The INSERT command.</returns>
    public new WitDbCommand GetInsertCommand()
    {
        return (WitDbCommand)base.GetInsertCommand();
    }

    /// <summary>
    /// Gets the automatically generated INSERT command.
    /// </summary>
    /// <param name="useColumnsForParameterNames">
    /// If true, generate parameter names from column names.
    /// </param>
    /// <returns>The INSERT command.</returns>
    public new WitDbCommand GetInsertCommand(bool useColumnsForParameterNames)
    {
        return (WitDbCommand)base.GetInsertCommand(useColumnsForParameterNames);
    }

    /// <summary>
    /// Gets the automatically generated UPDATE command.
    /// </summary>
    /// <returns>The UPDATE command.</returns>
    public new WitDbCommand GetUpdateCommand()
    {
        return (WitDbCommand)base.GetUpdateCommand();
    }

    /// <summary>
    /// Gets the automatically generated UPDATE command.
    /// </summary>
    /// <param name="useColumnsForParameterNames">
    /// If true, generate parameter names from column names.
    /// </param>
    /// <returns>The UPDATE command.</returns>
    public new WitDbCommand GetUpdateCommand(bool useColumnsForParameterNames)
    {
        return (WitDbCommand)base.GetUpdateCommand(useColumnsForParameterNames);
    }

    /// <summary>
    /// Gets the automatically generated DELETE command.
    /// </summary>
    /// <returns>The DELETE command.</returns>
    public new WitDbCommand GetDeleteCommand()
    {
        return (WitDbCommand)base.GetDeleteCommand();
    }

    /// <summary>
    /// Gets the automatically generated DELETE command.
    /// </summary>
    /// <param name="useColumnsForParameterNames">
    /// If true, generate parameter names from column names.
    /// </param>
    /// <returns>The DELETE command.</returns>
    public new WitDbCommand GetDeleteCommand(bool useColumnsForParameterNames)
    {
        return (WitDbCommand)base.GetDeleteCommand(useColumnsForParameterNames);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the data adapter.
    /// </summary>
    public new WitDbDataAdapter? DataAdapter
    {
        get => (WitDbDataAdapter?)base.DataAdapter;
        set => base.DataAdapter = value;
    }

    #endregion
}
