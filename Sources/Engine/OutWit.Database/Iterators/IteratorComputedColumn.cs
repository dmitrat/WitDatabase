using OutWit.Database.Expressions;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Sql;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Evaluates a virtual computed column for one row, on behalf of the iterators that read rows.
/// </summary>
/// <remarks>
/// <para>
/// One place rather than three. <c>IteratorTableScan</c>, <c>IteratorIndexSeek</c> and
/// <c>IteratorIndexRangeScan</c> each carried their own copy of this loop, and each copy ended in
/// <c>catch { values[i] = WitSqlValue.Null; }</c> - so a computed column that could not be evaluated
/// answered NULL for every row, on every read path, with nothing raised anywhere. Measured
/// 2026-08-01: <c>W AS (NoSuchFunc(V))</c> was created, accepted an insert and returned NULL, while
/// the <b>same</b> expression in a <c>CHECK</c> or a view threw. The engine disagreed with itself
/// about one failure.
/// </para>
/// <para>
/// NULL is the one value a caller cannot tell from a real answer, so this was a wrong result rather
/// than a missing one. It is the class the phase-8 audit named when a partial index reported itself
/// as complete: silent, plausible, and believed by whatever read it.
/// </para>
/// <para>
/// Sharing the code is not cosmetic here. The three copies are how a fix goes half-applied, and the
/// fixture that covers this asks each read path separately for that reason.
/// </para>
/// </remarks>
internal static class IteratorComputedColumn
{
    /// <summary>
    /// Computes one virtual column, or explains why it cannot be computed.
    /// </summary>
    /// <param name="evaluator">The evaluator for the current execution context.</param>
    /// <param name="expression">The column's stored expression tree.</param>
    /// <param name="row">The row's stored values, without the computed columns.</param>
    /// <param name="tableName">The table, for the message.</param>
    /// <param name="columnName">The column, for the message.</param>
    /// <exception cref="InvalidOperationException">
    /// The expression could not be evaluated. The inner exception carries what actually went wrong -
    /// an unknown function, or a column the expression names that the table no longer has.
    /// </exception>
    public static WitSqlValue Evaluate(
        ExpressionEvaluator evaluator,
        WitSqlExpression expression,
        WitSqlRow row,
        string tableName,
        string columnName)
    {
        try
        {
            return evaluator.Evaluate(expression, row);
        }
        catch (Exception ex)
        {
            // Named, because the caller is reading a table and has no way to guess which of its
            // columns is computed, let alone which one failed. The two shapes that reach here are an
            // unknown function and an expression left naming a column that RENAME COLUMN or DROP
            // COLUMN removed - the second is a recorded defect of its own, and answering NULL for it
            // is how it stayed quiet.
            throw new InvalidOperationException(
                $"The computed column {tableName}.{columnName} could not be evaluated: {ex.Message}", ex);
        }
    }
}
