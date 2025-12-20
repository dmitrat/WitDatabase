using OutWit.Database.Definitions;
using OutWit.Database.Types;

namespace OutWit.Database.Schema;

/// <summary>
/// Columns management part of SchemaCatalog.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Columns

    /// <summary>
    /// Adds a column to an existing table.
    /// </summary>
    public void AddColumn(string tableName, DefinitionColumn column)
    {
        if (!m_tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table '{tableName}' not found");

        var newColumns = table.Columns.ToList();
        var newColumn = new DefinitionColumn
        {
            Name = column.Name,
            Type = column.Type,
            Nullable = column.Nullable,
            IsPrimaryKey = column.IsPrimaryKey,
            IsAutoIncrement = column.IsAutoIncrement,
            IsUnique = column.IsUnique,
            DefaultValue = column.DefaultValue,
            Ordinal = newColumns.Count,
            CheckExpression = column.CheckExpression,
            ForeignKey = column.ForeignKey
        };
        newColumns.Add(newColumn);
        
        m_tables[tableName] = new DefinitionTable
        {
            Name = table.Name,
            Columns = newColumns,
            PrimaryKey = table.PrimaryKey,
            RowIdColumn = table.RowIdColumn,
            AutoIncrementRowId = table.AutoIncrementRowId,
            CheckExpressions = table.CheckExpressions,
            ForeignKeys = table.ForeignKeys,
            UniqueConstraints = table.UniqueConstraints
        };
        SaveSchema();
    }

    /// <summary>
    /// Drops a column from an existing table.
    /// </summary>
    public void DropColumn(string tableName, string columnName)
    {
        if (!m_tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table '{tableName}' not found");

        var newColumns = new List<DefinitionColumn>();
        int ordinal = 0;
        foreach (var c in table.Columns)
        {
            if (!c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                newColumns.Add(new DefinitionColumn
                {
                    Name = c.Name,
                    Type = c.Type,
                    Nullable = c.Nullable,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsAutoIncrement = c.IsAutoIncrement,
                    IsUnique = c.IsUnique,
                    DefaultValue = c.DefaultValue,
                    Ordinal = ordinal++,
                    CheckExpression = c.CheckExpression,
                    ForeignKey = c.ForeignKey
                });
            }
        }
        
        m_tables[tableName] = new DefinitionTable
        {
            Name = table.Name,
            Columns = newColumns,
            PrimaryKey = table.PrimaryKey,
            RowIdColumn = table.RowIdColumn,
            AutoIncrementRowId = table.AutoIncrementRowId,
            CheckExpressions = table.CheckExpressions,
            ForeignKeys = table.ForeignKeys,
            UniqueConstraints = table.UniqueConstraints
        };
        SaveSchema();
    }

    /// <summary>
    /// Renames a column in a table.
    /// </summary>
    public void RenameColumn(string tableName, string oldColumnName, string newColumnName)
    {
        if (!m_tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table '{tableName}' not found");

        var newColumns = table.Columns.Select(c => 
            c.Name.Equals(oldColumnName, StringComparison.OrdinalIgnoreCase) 
                ? new DefinitionColumn
                {
                    Name = newColumnName,
                    Type = c.Type,
                    Nullable = c.Nullable,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsAutoIncrement = c.IsAutoIncrement,
                    IsUnique = c.IsUnique,
                    DefaultValue = c.DefaultValue,
                    Ordinal = c.Ordinal,
                    CheckExpression = c.CheckExpression,
                    ForeignKey = c.ForeignKey
                }
                : c).ToList();
        
        m_tables[tableName] = new DefinitionTable
        {
            Name = table.Name,
            Columns = newColumns,
            PrimaryKey = table.PrimaryKey,
            RowIdColumn = table.RowIdColumn,
            AutoIncrementRowId = table.AutoIncrementRowId,
            CheckExpressions = table.CheckExpressions,
            ForeignKeys = table.ForeignKeys,
            UniqueConstraints = table.UniqueConstraints
        };
        SaveSchema();
    }

    /// <summary>
    /// Changes a column's data type.
    /// </summary>
    public void AlterColumnType(string tableName, string columnName, WitDataType newType)
    {
        if (!m_tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table '{tableName}' not found");

        var newColumns = table.Columns.Select(c => 
            c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase) 
                ? new DefinitionColumn
                {
                    Name = c.Name,
                    Type = newType,
                    Nullable = c.Nullable,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsAutoIncrement = c.IsAutoIncrement,
                    IsUnique = c.IsUnique,
                    DefaultValue = c.DefaultValue,
                    Ordinal = c.Ordinal,
                    CheckExpression = c.CheckExpression,
                    ForeignKey = c.ForeignKey
                }
                : c).ToList();

        m_tables[tableName] = new DefinitionTable
        {
            Name = table.Name,
            Columns = newColumns,
            PrimaryKey = table.PrimaryKey,
            RowIdColumn = table.RowIdColumn,
            AutoIncrementRowId = table.AutoIncrementRowId,
            CheckExpressions = table.CheckExpressions,
            ForeignKeys = table.ForeignKeys,
            UniqueConstraints = table.UniqueConstraints
        };
        SaveSchema();
    }

    /// <summary>
    /// Sets or clears a column's default value.
    /// </summary>
    public void SetColumnDefault(string tableName, string columnName, string? defaultValue)
    {
        if (!m_tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table '{tableName}' not found");

        var newColumns = table.Columns.Select(c => 
            c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase) 
                ? new DefinitionColumn
                {
                    Name = c.Name,
                    Type = c.Type,
                    Nullable = c.Nullable,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsAutoIncrement = c.IsAutoIncrement,
                    IsUnique = c.IsUnique,
                    DefaultValue = defaultValue,
                    Ordinal = c.Ordinal,
                    CheckExpression = c.CheckExpression,
                    ForeignKey = c.ForeignKey
                }
                : c).ToList();

        m_tables[tableName] = new DefinitionTable
        {
            Name = table.Name,
            Columns = newColumns,
            PrimaryKey = table.PrimaryKey,
            RowIdColumn = table.RowIdColumn,
            AutoIncrementRowId = table.AutoIncrementRowId,
            CheckExpressions = table.CheckExpressions,
            ForeignKeys = table.ForeignKeys,
            UniqueConstraints = table.UniqueConstraints
        };
        SaveSchema();
    }

    /// <summary>
    /// Sets or clears a column's NOT NULL constraint.
    /// </summary>
    public void SetColumnNotNull(string tableName, string columnName, bool notNull)
    {
        if (!m_tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table '{tableName}' not found");

        var newColumns = table.Columns.Select(c => 
            c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase) 
                ? new DefinitionColumn
                {
                    Name = c.Name,
                    Type = c.Type,
                    Nullable = !notNull,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsAutoIncrement = c.IsAutoIncrement,
                    IsUnique = c.IsUnique,
                    DefaultValue = c.DefaultValue,
                    Ordinal = c.Ordinal,
                    CheckExpression = c.CheckExpression,
                    ForeignKey = c.ForeignKey
                }
                : c).ToList();

        m_tables[tableName] = new DefinitionTable
        {
            Name = table.Name,
            Columns = newColumns,
            PrimaryKey = table.PrimaryKey,
            RowIdColumn = table.RowIdColumn,
            AutoIncrementRowId = table.AutoIncrementRowId,
            CheckExpressions = table.CheckExpressions,
            ForeignKeys = table.ForeignKeys,
            UniqueConstraints = table.UniqueConstraints
        };
        SaveSchema();
    }

    #endregion
}
