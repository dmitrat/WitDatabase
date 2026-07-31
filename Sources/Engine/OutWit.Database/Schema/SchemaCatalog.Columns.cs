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

            // Migrations are replayed routinely - a partially applied migration, a script run twice.
            // Without this the same column is appended again and every row is widened a second time,
            // leaving a catalog that holds one name twice and a table nothing can address.
            if (table.Columns.Any(c => string.Equals(c.Name, column.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Column '{column.Name}' already exists on table '{tableName}'");
            }

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

            // Dropping a column the primary key is built from is refused rather than performed.
            // It used to be accepted, leaving the key naming a column that no longer existed, and
            // the next INSERT died with "Column 'Id' not found" - a table nothing could write to.
            // Refusing is what SQLite does and is the only outcome that cannot corrupt the schema:
            // silently rewriting a table's identity is not something a DROP COLUMN should decide.
            if (table.PrimaryKey != null &&
                table.PrimaryKey.Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Cannot drop column '{columnName}' from table '{tableName}': it is part of the primary key");
            }

            var newColumns = new List<DefinitionColumn>();
            int ordinal = 0;
            foreach (var column in table.Columns)
            {
                if (!column.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    newColumns.Add(column.With(x => x.Ordinal, ordinal++));
                }
            }

            // Foreign keys built on the dropped column go with it. Leaving them behind is what made
            // the table un-insertable: validation walked a key whose column had gone.
            var newForeignKeys = table.ForeignKeys?
                .Where(fk => !fk.Columns.Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var newNamedConstraints = table.NamedConstraints?
                .Where(constraint => !ReferencesColumn(constraint, columnName))
                .ToList();

            m_tables[tableName] = table
                .With(x => x.Columns, newColumns)
                .With(x => x.ForeignKeys, newForeignKeys)
                .With(x => x.NamedConstraints, newNamedConstraints);

            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Whether a named constraint is built on the given column, and so cannot outlive it.
    /// </summary>
    private static bool ReferencesColumn(DefinitionNamedConstraint constraint, string columnName)
    {
        if (constraint.Columns?.Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase)) == true)
            return true;

        return constraint.ForeignKey?.Columns
            .Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase)) == true;
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
    /// Takes the dropped constraint's enforcement out of the table as well as its name.
    /// </summary>
    /// <remarks>
    /// A constraint declared inline in <c>CREATE TABLE</c> is recorded twice: once by name, and once in
    /// the structure that enforces it - <c>CheckExpressions</c>, <c>UniqueConstraints</c> or
    /// <c>ForeignKeys</c>. Those structures are what <c>INFORMATION_SCHEMA</c>, cascade handling and
    /// validation all read, so a named constraint cannot simply be kept out of them. Dropping therefore
    /// has to remove both halves, or the constraint keeps being enforced under no name at all - which is
    /// what it did until 2026-07-31: <c>DROP CONSTRAINT</c> was accepted and changed nothing.
    ///
    /// <b>Exactly one match is removed.</b> An identical constraint declared anonymously alongside a
    /// named one is a different constraint, and dropping the named one must not take it with it.
    /// </remarks>
    private static DefinitionTable RemoveEnforcementFor(DefinitionNamedConstraint dropped, DefinitionTable table)
    {
        switch (dropped.Type)
        {
            case ConstraintType.Check when dropped.CheckExpression != null && table.CheckExpressions != null:
            {
                var remaining = RemoveFirst(table.CheckExpressions,
                    e => string.Equals(e, dropped.CheckExpression, StringComparison.Ordinal));

                return table.With(x => x.CheckExpressions, remaining.Count > 0 ? remaining : null);
            }

            case ConstraintType.Unique when dropped.Columns != null && table.UniqueConstraints != null:
            {
                var remaining = RemoveFirst(table.UniqueConstraints, u => SameColumns(u, dropped.Columns));

                var updated = table.With(x => x.UniqueConstraints, remaining.Count > 0 ? remaining : null);

                // A single-column UNIQUE also marks the column itself, and validation reads that mark, so
                // leaving it behind would keep refusing duplicates after the constraint was dropped.
                // Cleared only when nothing else still covers the column - another UNIQUE, named or not.
                if (dropped.Columns.Count == 1)
                {
                    var column = dropped.Columns[0];

                    var stillCovered =
                        remaining.Any(u => u.Count == 1 && SameColumns(u, dropped.Columns))
                        || (updated.NamedConstraints?.Any(c =>
                                c.Type == ConstraintType.Unique && SameColumns(c.Columns, dropped.Columns))
                            ?? false);

                    if (!stillCovered)
                    {
                        foreach (var col in updated.Columns.Where(c =>
                                     c.Name.Equals(column, StringComparison.OrdinalIgnoreCase)))
                        {
                            col.IsUnique = false;
                        }
                    }
                }

                return updated;
            }

            case ConstraintType.ForeignKey when dropped.Columns != null && table.ForeignKeys != null:
            {
                var remaining = RemoveFirst(table.ForeignKeys, fk => SameColumns(fk.Columns, dropped.Columns));

                return table.With(x => x.ForeignKeys, remaining.Count > 0 ? remaining : null);
            }

            default:
                return table;
        }
    }

    private static List<T> RemoveFirst<T>(IReadOnlyList<T> source, Func<T, bool> match)
    {
        var remaining = source.ToList();
        var index = remaining.FindIndex(x => match(x));

        if (index >= 0)
            remaining.RemoveAt(index);

        return remaining;
    }

    private static bool SameColumns(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        left != null && right != null
        && left.Count == right.Count
        && left.Zip(right, (a, b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase)).All(x => x);

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

            var dropped = table.NamedConstraints
                .First(c => c.Name.Equals(constraintName, StringComparison.OrdinalIgnoreCase));

            var updated = table.With(x => x.NamedConstraints, constraints.Count > 0 ? constraints : null);

            m_tables[tableName] = RemoveEnforcementFor(dropped, updated);
            SaveSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    #endregion
}
