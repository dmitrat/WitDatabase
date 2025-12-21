using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser
{
    /// <summary>
    /// Result of parsing SQL text.
    /// </summary>
    public sealed class WitSqlParsingResult : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not WitSqlParsingResult other)
                return false;

            return Statements.Is(other.Statements)
                   && Errors.Is(other.Errors);
        }

        public override WitSqlParsingResult Clone()
        {
            return new WitSqlParsingResult
            {
                Statements = Statements.Select(statement => (WitSqlStatement)statement.Clone()).ToList(),
                Errors = Errors.Select(error => error.Clone()).ToList(),
            };
        }

        #endregion

        #region Properties

        [ToString]
        public bool IsSuccess => Errors.Count == 0;
        public IReadOnlyList<WitSqlStatement> Statements { get; init; } = [];
        public IReadOnlyList<WitSqlParsingError> Errors { get; init; } = [];

        #endregion
    }
}


