namespace OutWit.Database.Studio.Models;

/// <summary>
/// A function or a procedure, as the catalogue holds it.
///
/// The tree had no folder for these at all, while the engine has had them since phase 9d - a client
/// that cannot show what the database contains is telling the user it is not there.
/// </summary>
public sealed class RoutineInfo
{
    public required string Name { get; init; }

    /// <summary>
    /// FUNCTION or PROCEDURE, as INFORMATION_SCHEMA.ROUTINES spells it.
    /// </summary>
    public required string RoutineType { get; init; }

    /// <summary>
    /// What a function returns; null for a procedure.
    /// </summary>
    public string? DataType { get; init; }

    public string? Definition { get; init; }

    public bool IsFunction => RoutineType.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => IsFunction ? $"{Name}() -> {DataType}" : $"{Name}()";
}

/// <summary>
/// One foreign key, in the direction it was declared.
/// </summary>
public sealed class ForeignKeyInfo
{
    public required string ConstraintName { get; init; }

    public required string FromTable { get; init; }

    public required string FromColumn { get; init; }

    public required string ToTable { get; init; }

    public required string ToColumn { get; init; }

    /// <summary>
    /// What happens to this row when the referenced one is deleted or its key changes. Read from
    /// REFERENTIAL_CONSTRAINTS; used when the DDL of the table is written out.
    /// </summary>
    public string? OnDelete { get; init; }

    public string? OnUpdate { get; init; }

    public override string ToString() => $"{FromTable}.{FromColumn} -> {ToTable}.{ToColumn}";
}

/// <summary>
/// One index, with the columns it covers.
/// </summary>
public sealed class IndexInfo
{
    public required string Name { get; init; }

    public required string TableName { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }

    public bool IsUnique { get; init; }

    public string? FilterCondition { get; init; }

    public override string ToString()
    {
        var kind = IsUnique ? "UNIQUE " : string.Empty;

        return $"{kind}{Name} ({string.Join(", ", Columns)})";
    }
}

/// <summary>
/// What the inspector says about getting at a table's data (2.5).
///
/// This is the part of the Explorer that knows the engine: an index is not created for a PRIMARY KEY
/// automatically here, so a table can have a key and no index on it - and an insert with an explicit
/// key then degrades quadratically. Saying which columns have an index, and which do not, is a fact
/// the catalogue answers, and it is the fact that predicts the slowness.
/// </summary>
public sealed class DataAccessNote
{
    public required string Column { get; init; }

    public required bool IsIndexed { get; init; }

    public string? IndexName { get; init; }

    public bool IsPrimaryKey { get; init; }

    public override string ToString()
    {
        if (IsIndexed)
            return $"{Column}: {IndexName}";

        return IsPrimaryKey
            ? $"{Column}: primary key with no index"
            : $"{Column}: no index";
    }
}

/// <summary>
/// One line of the filter's result: the node that matched and the path that leads to it (2.3).
///
/// The path is what makes a match usable with several connections open - "Orders" on its own does not
/// say which database, and a filter that answers ambiguously is worse than one that answers slowly.
/// </summary>
public sealed record FilterMatch(DatabaseNode Node, string Path)
{
    public override string ToString() => $"{Node.Name} - {Path}";
}
