using System.Data;
using System.Data.Common;

namespace OutWit.Database.AdoNet;

/// <summary>
/// Represents a parameter to a <see cref="WitDbCommand"/>.
/// </summary>
public sealed class WitDbParameter : DbParameter, ICloneable
{
    #region Fields

    private string m_parameterName = string.Empty;
    private object? m_value;
    private DbType m_dbType = DbType.Object;
    private ParameterDirection m_direction = ParameterDirection.Input;
    private bool m_isNullable = true;
    private int m_size;
    private byte m_precision;
    private byte m_scale;
    private string m_sourceColumn = string.Empty;
    private bool m_sourceColumnNullMapping;
    private DataRowVersion m_sourceVersion = DataRowVersion.Current;
    private bool m_dbTypeExplicitlySet;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbParameter"/> class.
    /// </summary>
    public WitDbParameter()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbParameter"/> class
    /// with the specified name and value.
    /// </summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="value">The parameter value.</param>
    public WitDbParameter(string parameterName, object? value)
    {
        ParameterName = parameterName;
        Value = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbParameter"/> class
    /// with the specified name and type.
    /// </summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="dbType">The parameter type.</param>
    public WitDbParameter(string parameterName, DbType dbType)
    {
        ParameterName = parameterName;
        DbType = dbType;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbParameter"/> class
    /// with the specified name, type, and size.
    /// </summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="dbType">The parameter type.</param>
    /// <param name="size">The size of the parameter.</param>
    public WitDbParameter(string parameterName, DbType dbType, int size)
    {
        ParameterName = parameterName;
        DbType = dbType;
        Size = size;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbParameter"/> class
    /// with the specified name, type, size, and source column.
    /// </summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="dbType">The parameter type.</param>
    /// <param name="size">The size of the parameter.</param>
    /// <param name="sourceColumn">The source column name.</param>
    public WitDbParameter(string parameterName, DbType dbType, int size, string sourceColumn)
    {
        ParameterName = parameterName;
        DbType = dbType;
        Size = size;
        SourceColumn = sourceColumn;
    }

    #endregion

    #region Functions

    /// <inheritdoc/>
    public override void ResetDbType()
    {
        m_dbType = DbType.Object;
        m_dbTypeExplicitlySet = false;
    }

    /// <summary>
    /// Creates a copy of this parameter.
    /// </summary>
    /// <returns>A new <see cref="WitDbParameter"/> with the same values.</returns>
    public WitDbParameter Clone()
    {
        return new WitDbParameter
        {
            m_parameterName = m_parameterName,
            m_value = m_value,
            m_dbType = m_dbType,
            m_direction = m_direction,
            m_isNullable = m_isNullable,
            m_size = m_size,
            m_precision = m_precision,
            m_scale = m_scale,
            m_sourceColumn = m_sourceColumn,
            m_sourceColumnNullMapping = m_sourceColumnNullMapping,
            m_sourceVersion = m_sourceVersion,
            m_dbTypeExplicitlySet = m_dbTypeExplicitlySet
        };
    }

    /// <inheritdoc/>
    object ICloneable.Clone() => Clone();

    private static DbType InferDbType(object? value)
    {
        return value switch
        {
            null or DBNull => DbType.Object,
            bool => DbType.Boolean,
            sbyte => DbType.SByte,
            byte => DbType.Byte,
            short => DbType.Int16,
            ushort => DbType.UInt16,
            int => DbType.Int32,
            uint => DbType.UInt32,
            long => DbType.Int64,
            ulong => DbType.UInt64,
            float => DbType.Single,
            double => DbType.Double,
            decimal => DbType.Decimal,
            string => DbType.String,
            byte[] => DbType.Binary,
            DateTime => DbType.DateTime,
            DateOnly => DbType.Date,
            TimeOnly => DbType.Time,
            TimeSpan => DbType.Time,
            Guid => DbType.Guid,
            DateTimeOffset => DbType.DateTimeOffset,
            _ => DbType.Object
        };
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override string ParameterName
    {
        get => m_parameterName;
        set => m_parameterName = value ?? string.Empty;
    }

    /// <inheritdoc/>
    public override object? Value
    {
        get => m_value;
        set
        {
            m_value = value;
            
            // Infer DbType from value if not explicitly set
            if (!m_dbTypeExplicitlySet)
            {
                m_dbType = InferDbType(value);
            }
        }
    }

    /// <inheritdoc/>
    public override DbType DbType
    {
        get => m_dbType;
        set
        {
            m_dbType = value;
            m_dbTypeExplicitlySet = true;
        }
    }

    /// <inheritdoc/>
    public override ParameterDirection Direction
    {
        get => m_direction;
        set
        {
            if (value != ParameterDirection.Input)
                throw new NotSupportedException("Only ParameterDirection.Input is supported.");
            m_direction = value;
        }
    }

    /// <inheritdoc/>
    public override bool IsNullable
    {
        get => m_isNullable;
        set => m_isNullable = value;
    }

    /// <inheritdoc/>
    public override int Size
    {
        get => m_size;
        set => m_size = value;
    }

    /// <summary>
    /// Gets or sets the precision for numeric parameters.
    /// </summary>
    /// <remarks>
    /// <c>override</c> matters here rather than being a formality: the base declares this virtual, so a
    /// consumer holding a <see cref="DbParameter"/> - which is what <c>CreateParameter</c> returns -
    /// wrote into the base class's own storage and the value never reached this provider.
    /// </remarks>
    public override byte Precision
    {
        get => m_precision;
        set => m_precision = value;
    }

    /// <summary>
    /// Gets or sets the scale for numeric parameters.
    /// </summary>
    /// <remarks>See <see cref="Precision"/> - the same gap, and the same reason it was invisible.</remarks>
    public override byte Scale
    {
        get => m_scale;
        set => m_scale = value;
    }

    /// <inheritdoc/>
    public override string SourceColumn
    {
        get => m_sourceColumn;
        set => m_sourceColumn = value ?? string.Empty;
    }

    /// <inheritdoc/>
    public override bool SourceColumnNullMapping
    {
        get => m_sourceColumnNullMapping;
        set => m_sourceColumnNullMapping = value;
    }

    /// <inheritdoc/>
    public override DataRowVersion SourceVersion
    {
        get => m_sourceVersion;
        set => m_sourceVersion = value;
    }

    #endregion
}
