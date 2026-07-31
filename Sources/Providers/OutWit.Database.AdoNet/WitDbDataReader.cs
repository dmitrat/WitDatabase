using System.Collections;
using System.Data;
using System.Data.Common;
using OutWit.Database.Sql;
using OutWit.Database.Types;

namespace OutWit.Database.AdoNet;

/// <summary>
/// Provides a way of reading a forward-only stream of rows from a WitDatabase database.
/// </summary>
public sealed class WitDbDataReader : DbDataReader
{
    #region Fields

    private WitSqlResult? m_result;
    private readonly WitDbConnection m_connection;
    private readonly CommandBehavior m_behavior;
    private bool m_isClosed;
    private WitSqlRow m_currentRow;
    private bool m_hasRead;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbDataReader"/> class.
    /// </summary>
    /// <param name="result">The result set to read.</param>
    /// <param name="connection">The connection that created this reader.</param>
    /// <param name="behavior">The command behavior.</param>
    internal WitDbDataReader(WitSqlResult result, WitDbConnection connection, CommandBehavior behavior)
    {
        m_result = result;
        m_connection = connection;
        m_behavior = behavior;
    }

    #endregion

    #region Read

    /// <inheritdoc/>
    public override bool Read()
    {
        EnsureOpen();

        if (m_result == null || !m_result.HasRows)
            return false;

        if (m_result.Read())
        {
            m_currentRow = m_result.CurrentRow;
            m_hasRead = true;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(Read, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override bool NextResult()
    {
        // WitDatabase doesn't support multiple result sets in a single command yet
        return false;
    }

    /// <inheritdoc/>
    public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(NextResult, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Close

    /// <inheritdoc/>
    public override void Close()
    {
        if (m_isClosed)
            return;

        m_result?.Dispose();
        m_result = null;
        m_isClosed = true;

        // Told before anything else, so a connection closing this reader does not then wait for it.
        m_connection.UnregisterReader(this);

        if ((m_behavior & CommandBehavior.CloseConnection) != 0)
        {
            m_connection.Close();
        }
    }

    /// <inheritdoc/>
    public override async Task CloseAsync()
    {
        await Task.Run(Close).ConfigureAwait(false);
    }

    #endregion

    #region Column Metadata

    /// <inheritdoc/>
    public override string GetName(int ordinal)
    {
        EnsureOpen();
        ValidateOrdinal(ordinal);
        return m_result!.Columns[ordinal].Name;
    }

    /// <inheritdoc/>
    public override int GetOrdinal(string name)
    {
        EnsureOpen();

        for (int i = 0; i < m_result!.Columns.Count; i++)
        {
            if (string.Equals(m_result.Columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        // Try matching by suffix (column name without table prefix)
        if (!name.Contains('.'))
        {
            var suffix = "." + name;
            for (int i = 0; i < m_result.Columns.Count; i++)
            {
                if (m_result.Columns[i].Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        throw new ArgumentException($"Column '{name}' not found.", nameof(name));
    }

    /// <inheritdoc/>
    public override string GetDataTypeName(int ordinal)
    {
        EnsureOpen();
        ValidateOrdinal(ordinal);
        return m_result!.Columns[ordinal].Type.ToString();
    }

    /// <inheritdoc/>
    public override Type GetFieldType(int ordinal)
    {
        EnsureOpen();
        ValidateOrdinal(ordinal);
        return MapTypeToClr(m_result!.Columns[ordinal].Type);
    }

    private static Type MapTypeToClr(WitSqlType sqlType)
    {
        return sqlType switch
        {
            WitSqlType.Null => typeof(DBNull),
            WitSqlType.Integer => typeof(long),
            WitSqlType.Real => typeof(double),
            WitSqlType.Text => typeof(string),
            WitSqlType.Blob => typeof(byte[]),
            WitSqlType.Boolean => typeof(bool),
            WitSqlType.Decimal => typeof(decimal),
            WitSqlType.DateTime => typeof(DateTime),
            WitSqlType.DateOnly => typeof(DateOnly),
            WitSqlType.TimeOnly => typeof(TimeOnly),
            WitSqlType.TimeSpan => typeof(TimeSpan),
            WitSqlType.Guid => typeof(Guid),
            WitSqlType.DateTimeOffset => typeof(DateTimeOffset),
            WitSqlType.Json => typeof(string),
            WitSqlType.RowVersion => typeof(byte[]),
            _ => typeof(object)
        };
    }

    #endregion

    #region GetValue

    /// <inheritdoc/>
    public override object GetValue(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);

        var value = m_currentRow[ordinal];
        return value.IsNull ? DBNull.Value : value.ToObject() ?? DBNull.Value;
    }

    /// <inheritdoc/>
    public override int GetValues(object[] values)
    {
        EnsureHasRead();

        var count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        return count;
    }

    /// <inheritdoc/>
    public override bool IsDBNull(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].IsNull;
    }

    /// <inheritdoc/>
    public override async Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        return await Task.Run(() => IsDBNull(ordinal), cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Typed Getters

    /// <inheritdoc/>
    public override bool GetBoolean(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].AsBool();
    }

    /// <inheritdoc/>
    public override byte GetByte(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return (byte)m_currentRow[ordinal].AsInt64();
    }

    /// <inheritdoc/>
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);

        var bytes = m_currentRow[ordinal].AsBlob();
        if (buffer == null)
            return bytes.Length;

        var available = bytes.Length - (int)dataOffset;
        var toCopy = Math.Min(available, length);
        
        if (toCopy > 0)
        {
            Array.Copy(bytes, (int)dataOffset, buffer, bufferOffset, toCopy);
        }

        return toCopy;
    }

    /// <inheritdoc/>
    public override char GetChar(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        
        var str = m_currentRow[ordinal].AsString();
        return string.IsNullOrEmpty(str) ? '\0' : str[0];
    }

    /// <inheritdoc/>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);

        var str = m_currentRow[ordinal].AsString();
        if (buffer == null)
            return str.Length;

        var available = str.Length - (int)dataOffset;
        var toCopy = Math.Min(available, length);
        
        if (toCopy > 0)
        {
            str.CopyTo((int)dataOffset, buffer, bufferOffset, toCopy);
        }

        return toCopy;
    }

    /// <inheritdoc/>
    public override DateTime GetDateTime(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].AsDateTime();
    }

    /// <inheritdoc/>
    public override decimal GetDecimal(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].AsDecimal();
    }

    /// <inheritdoc/>
    public override double GetDouble(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].AsDouble();
    }

    /// <inheritdoc/>
    public override float GetFloat(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return (float)m_currentRow[ordinal].AsDouble();
    }

    /// <inheritdoc/>
    public override Guid GetGuid(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].AsGuid();
    }

    /// <inheritdoc/>
    public override short GetInt16(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return (short)m_currentRow[ordinal].AsInt64();
    }

    /// <inheritdoc/>
    public override int GetInt32(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return (int)m_currentRow[ordinal].AsInt64();
    }

    /// <inheritdoc/>
    public override long GetInt64(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].AsInt64();
    }

    /// <inheritdoc/>
    public override string GetString(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].AsString();
    }

    /// <summary>
    /// Gets the value of the specified column as a <see cref="DateOnly"/>.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The value of the column.</returns>
    public DateOnly GetDateOnly(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].AsDateOnly();
    }

    /// <summary>
    /// Gets the value of the specified column as a <see cref="TimeOnly"/>.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The value of the column.</returns>
    public TimeOnly GetTimeOnly(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].AsTimeOnly();
    }

    /// <summary>
    /// Gets the value of the specified column as a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The value of the column.</returns>
    public TimeSpan GetTimeSpan(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].AsTimeSpan();
    }

    /// <summary>
    /// Gets the value of the specified column as a <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The value of the column.</returns>
    public DateTimeOffset GetDateTimeOffset(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);
        return m_currentRow[ordinal].AsDateTimeOffset();
    }

    /// <inheritdoc/>
    public override T GetFieldValue<T>(int ordinal)
    {
        EnsureHasRead();
        ValidateOrdinal(ordinal);

        var value = m_currentRow[ordinal];
        
        if (value.IsNull)
        {
            if (default(T) == null)
                return default!;
            throw new InvalidCastException($"Cannot convert NULL to {typeof(T).Name}");
        }

        // Handle specific types
        if (typeof(T) == typeof(bool)) return (T)(object)value.AsBool();
        if (typeof(T) == typeof(byte)) return (T)(object)(byte)value.AsInt64();
        if (typeof(T) == typeof(sbyte)) return (T)(object)(sbyte)value.AsInt64();
        if (typeof(T) == typeof(short)) return (T)(object)(short)value.AsInt64();
        if (typeof(T) == typeof(ushort)) return (T)(object)(ushort)value.AsInt64();
        if (typeof(T) == typeof(int)) return (T)(object)(int)value.AsInt64();
        if (typeof(T) == typeof(uint)) return (T)(object)(uint)value.AsInt64();
        if (typeof(T) == typeof(long)) return (T)(object)value.AsInt64();
        if (typeof(T) == typeof(ulong)) return (T)(object)value.AsUInt64();
        if (typeof(T) == typeof(float)) return (T)(object)(float)value.AsDouble();
        if (typeof(T) == typeof(double)) return (T)(object)value.AsDouble();
        if (typeof(T) == typeof(decimal)) return (T)(object)value.AsDecimal();
        if (typeof(T) == typeof(string)) return (T)(object)value.AsString();
        if (typeof(T) == typeof(byte[])) return (T)(object)value.AsBlob();
        if (typeof(T) == typeof(DateTime)) return (T)(object)value.AsDateTime();
        if (typeof(T) == typeof(DateOnly)) return (T)(object)value.AsDateOnly();
        if (typeof(T) == typeof(TimeOnly)) return (T)(object)value.AsTimeOnly();
        if (typeof(T) == typeof(TimeSpan)) return (T)(object)value.AsTimeSpan();
        if (typeof(T) == typeof(Guid)) return (T)(object)value.AsGuid();
        if (typeof(T) == typeof(DateTimeOffset)) return (T)(object)value.AsDateTimeOffset();

        // Fallback
        var obj = value.ToObject();
        if (obj is T t)
            return t;

        return (T)Convert.ChangeType(obj!, typeof(T));
    }

    /// <inheritdoc/>
    public override async Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
    {
        return await Task.Run(() => GetFieldValue<T>(ordinal), cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Schema

    /// <inheritdoc/>
    public override DataTable? GetSchemaTable()
    {
        EnsureOpen();

        if (m_result == null || m_result.Columns.Count == 0)
            return null;

        var schema = new DataTable("SchemaTable");

        // Add standard schema columns
        schema.Columns.Add("ColumnName", typeof(string));
        schema.Columns.Add("ColumnOrdinal", typeof(int));
        schema.Columns.Add("ColumnSize", typeof(int));
        schema.Columns.Add("NumericPrecision", typeof(short));
        schema.Columns.Add("NumericScale", typeof(short));
        schema.Columns.Add("IsUnique", typeof(bool));
        schema.Columns.Add("IsKey", typeof(bool));
        schema.Columns.Add("BaseServerName", typeof(string));
        schema.Columns.Add("BaseCatalogName", typeof(string));
        schema.Columns.Add("BaseColumnName", typeof(string));
        schema.Columns.Add("BaseSchemaName", typeof(string));
        schema.Columns.Add("BaseTableName", typeof(string));
        schema.Columns.Add("DataType", typeof(Type));
        schema.Columns.Add("AllowDBNull", typeof(bool));
        schema.Columns.Add("ProviderType", typeof(int));
        schema.Columns.Add("IsAliased", typeof(bool));
        schema.Columns.Add("IsExpression", typeof(bool));
        schema.Columns.Add("IsIdentity", typeof(bool));
        schema.Columns.Add("IsAutoIncrement", typeof(bool));
        schema.Columns.Add("IsRowVersion", typeof(bool));
        schema.Columns.Add("IsHidden", typeof(bool));
        schema.Columns.Add("IsLong", typeof(bool));
        schema.Columns.Add("IsReadOnly", typeof(bool));
        schema.Columns.Add("ProviderSpecificDataType", typeof(Type));
        schema.Columns.Add("DataTypeName", typeof(string));

        for (int i = 0; i < m_result.Columns.Count; i++)
        {
            var col = m_result.Columns[i];
            var row = schema.NewRow();

            row["ColumnName"] = col.Name;
            row["ColumnOrdinal"] = i;
            row["ColumnSize"] = -1;
            row["NumericPrecision"] = DBNull.Value;
            row["NumericScale"] = DBNull.Value;
            row["IsUnique"] = false;
            row["IsKey"] = false;
            row["BaseServerName"] = string.Empty;
            row["BaseCatalogName"] = string.Empty;
            row["BaseColumnName"] = col.Name;
            row["BaseSchemaName"] = string.Empty;
            row["BaseTableName"] = col.TableName ?? string.Empty;
            row["DataType"] = MapTypeToClr(col.Type);
            row["AllowDBNull"] = col.IsNullable;
            row["ProviderType"] = (int)col.Type;
            row["IsAliased"] = false;
            row["IsExpression"] = false;
            row["IsIdentity"] = false;
            row["IsAutoIncrement"] = false;
            row["IsRowVersion"] = col.Type == WitSqlType.RowVersion;
            row["IsHidden"] = false;
            row["IsLong"] = col.Type is WitSqlType.Blob or WitSqlType.Text;
            row["IsReadOnly"] = true;
            row["ProviderSpecificDataType"] = MapTypeToClr(col.Type);
            row["DataTypeName"] = col.Type.ToString();

            schema.Rows.Add(row);
        }

        return schema;
    }

    #endregion

    #region Enumerator

    /// <inheritdoc/>
    public override IEnumerator GetEnumerator()
    {
        return new DbEnumerator(this, closeReader: false);
    }

    #endregion

    #region Helpers

    private void EnsureOpen()
    {
        if (m_isClosed)
            throw new InvalidOperationException("DataReader is closed.");
    }

    private void EnsureHasRead()
    {
        EnsureOpen();

        if (!m_hasRead)
            throw new InvalidOperationException("No data available. Call Read() first.");
    }

    private void ValidateOrdinal(int ordinal)
    {
        if (m_result == null || ordinal < 0 || ordinal >= m_result.Columns.Count)
            throw new IndexOutOfRangeException($"Column ordinal {ordinal} is out of range.");
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }
        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    #endregion

    #region Indexers

    /// <inheritdoc/>
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc/>
    public override object this[string name] => GetValue(GetOrdinal(name));

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override int FieldCount
    {
        get
        {
            EnsureOpen();
            return m_result?.Columns.Count ?? 0;
        }
    }

    /// <inheritdoc/>
    public override bool HasRows
    {
        get
        {
            EnsureOpen();
            return m_result?.HasRows ?? false;
        }
    }

    /// <inheritdoc/>
    public override bool IsClosed => m_isClosed;

    /// <inheritdoc/>
    public override int RecordsAffected => m_result?.RowsAffected ?? -1;

    /// <inheritdoc/>
    public override int Depth => 0;

    #endregion
}
