namespace OutWit.Database.Studio.Models;

/// <summary>
/// What one statement of a script did.
///
/// A script is executed one statement at a time (WS-22), so there is one of these per statement
/// rather than a single verdict for the whole text. It is what lets the Messages tab say "[2] INSERT
/// 312 rows - 96 ms" instead of "Query executed successfully".
/// </summary>
public sealed class StatementOutcome
{
    /// <summary>
    /// 1-based position in the script - the number a person counts to.
    /// </summary>
    public required int Number { get; init; }

    /// <summary>
    /// The first line of the statement, cut short: enough to recognise it in a list.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Rows returned for a SELECT, rows changed for anything else.
    /// </summary>
    public int RowsAffected { get; init; }

    /// <summary>
    /// Whether the statement produced a result set. A SELECT that matched nothing did; an INSERT
    /// did not, and the difference is what the count above means.
    /// </summary>
    public bool ReturnedRows { get; init; }

    public double ExecutionTimeMs { get; init; }

    /// <summary>
    /// Null when the statement succeeded. Short form: the engine's own message can run to well over
    /// a thousand characters of expected tokens.
    /// </summary>
    public string? ErrorMessage { get; init; }

    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);

    public override string ToString()
    {
        if (!IsSuccess)
            return $"[{Number}] {Summary} - failed: {ErrorMessage}";

        var what = ReturnedRows ? "returned" : "affected";

        return $"[{Number}] {Summary} - {RowsAffected} rows {what}, {ExecutionTimeMs:F0} ms";
    }
}
