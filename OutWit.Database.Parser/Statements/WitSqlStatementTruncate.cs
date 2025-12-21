using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    /// <summary>
    /// Represents a TRUNCATE TABLE statement.
    /// Removes all rows from a table, faster than DELETE without WHERE.
    /// </summary>
    public class WitSqlStatementTruncate : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementTruncate(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementTruncate truncate)
                return false;

            return base.Is(truncate, tolerance)
                   && TableName.Is(truncate.TableName);
        }

        public override WitSqlStatementTruncate Clone()
        {
            return new WitSqlStatementTruncate
            {
                Line = Line,
                Column = Column,
                TableName = TableName
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// The name of the table to truncate.
        /// </summary>
        [ToString]
        public required string TableName { get; init; }

        #endregion
    }
}
