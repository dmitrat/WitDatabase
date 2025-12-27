using OutWit.Common.Utils;
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
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.TryGetValue(tableName, out var table))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            List<DefinitionColumn> newColumns = table.Columns.ToList();
            var newColumn = column.With(x => x.Ordinal, newColumns.Count);
            newColumns.Add(newColumn);

            m_tables[tableName] = table.With(x => x.Columns, newColumns);
            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Drops a column from an existing table.
    /// </summary>
    public void DropColumn(string tableName, string columnName)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.TryGetValue(tableName, out var table))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            var newColumns = new List<DefinitionColumn>();
            int ordinal = 0;
            foreach (var column in table.Columns)
            {
                if (!column.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    newColumns.Add(column.With(x => x.Ordinal, ordinal++));
                }
            }

            m_tables[tableName] = table.With(x => x.Columns, newColumns);
            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Renames a column in a table.
    /// </summary>
    public void RenameColumn(string tableName, string oldColumnName, string newColumnName)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.TryGetValue(tableName, out var table))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            var newColumns = table.Columns
                .Select(column => column.Name.Equals(oldColumnName, StringComparison.OrdinalIgnoreCase)
                    ? column.With(x => x.Name, newColumnName)
                    : column)
                .ToList();

            m_tables[tableName] = table.With(x => x.Columns, newColumns);
            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Changes a column's data type.
    /// </summary>
    public void AlterColumnType(string tableName, string columnName, WitDataType newType, int? maxLength = null, int? precision = null, int? scale = null)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.TryGetValue(tableName, out var table))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            var newColumns = table.Columns
                .Select(column => column.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase)
                    ? column.With(x => x.Type, newType)
                        .With(x => x.MaxLength, maxLength ?? column.MaxLength)
                        .With(x => x.Precision, precision ?? column.Precision)
                        .With(x => x.Scale, scale ?? column.Scale)
                    : column)
                .ToList();

            m_tables[tableName] = table.With(x => x.Columns, newColumns);
            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Sets or clears a column's default value.
    /// </summary>
    public void SetColumnDefault(string tableName, string columnName, string? defaultValue)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.TryGetValue(tableName, out var table))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            var newColumns = table.Columns
                .Select(column => column.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase)
                    ? column.With(x => x.DefaultValue, defaultValue)
                    : column)
                .ToList();

            m_tables[tableName] = table.With(x => x.Columns, newColumns);
            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Sets or clears a column's NOT NULL constraint.
    /// </summary>
    public void SetColumnNotNull(string tableName, string columnName, bool notNull)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.TryGetValue(tableName, out var table))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            var newColumns = table.Columns
                .Select(column => column.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase)
                    ? column.With(x => x.Nullable, !notNull)
                    : column)
                .ToList();

            m_tables[tableName] = table.With(x => x.Columns, newColumns);
            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Sets or clears a column's collation.
    /// </summary>
    public void SetColumnCollation(string tableName, string columnName, string? collation)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.TryGetValue(tableName, out var table))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            var newColumns = table.Columns
                .Select(column => column.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase)
                    ? column.With(x => x.Collation, collation)
                    : column)
                .ToList();

            m_tables[tableName] = table.With(x => x.Columns, newColumns);
            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Adds a named constraint to an existing table.
    /// </summary>
    public void AddConstraint(string tableName, DefinitionNamedConstraint constraint)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.TryGetValue(tableName, out var table))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            var constraints = table.NamedConstraints?.ToList() ?? [];
            constraints.Add(constraint);

            m_tables[tableName] = table.With(x => x.NamedConstraints, constraints);
            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Drops a named constraint from an existing table.
    /// </summary>
    public void DropConstraint(string tableName, string constraintName)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.TryGetValue(tableName, out var table))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            if (table.NamedConstraints == null)
                throw new InvalidOperationException($"Constraint '{constraintName}' not found on table '{tableName}'");

            var constraints = table.NamedConstraints
                .Where(c => !c.Name.Equals(constraintName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (constraints.Count == table.NamedConstraints.Count)
                throw new InvalidOperationException($"Constraint '{constraintName}' not found on table '{tableName}'");

            m_tables[tableName] = table.With(x => x.NamedConstraints, constraints.Count > 0 ? constraints : null);
            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    #endregion
}
