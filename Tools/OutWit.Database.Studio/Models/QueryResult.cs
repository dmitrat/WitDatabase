using OutWit.Common.Abstract;
using System.Data;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// Result of executing a query.
/// </summary>
public sealed class QueryResult : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not QueryResult other)
            return false;

        return RowsAffected == other.RowsAffected
            && ExecutionTimeMs == other.ExecutionTimeMs
            && ErrorMessage == other.ErrorMessage;
    }

    public override QueryResult Clone()
    {
        var clone = new QueryResult
        {
            RowsAffected = RowsAffected,
            ExecutionTimeMs = ExecutionTimeMs,
            ErrorMessage = ErrorMessage,
            ResultTable = ResultTable?.Copy()
        };

        return clone;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the number of rows affected (for INSERT, UPDATE, DELETE).
    /// </summary>
    public int RowsAffected { get; set; }

    /// <summary>
    /// Gets or sets the execution time in milliseconds.
    /// </summary>
    public double ExecutionTimeMs { get; set; }

    /// <summary>
    /// Gets or sets the error message (if any).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the result data table (for SELECT queries).
    /// </summary>
    public DataTable? ResultTable { get; set; }

    /// <summary>
    /// Gets whether the query was successful.
    /// </summary>
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);

    #endregion
}
