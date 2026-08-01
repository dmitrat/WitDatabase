using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Clauses;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementCreateIndex : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementCreateIndex(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementCreateIndex create)
                return false;

            return base.Is(create, tolerance)
                   && IndexName.Is(create.IndexName)
                   && TableName.Is(create.TableName)
                   && IsUnique.Is(create.IsUnique)
                   && IfNotExists.Is(create.IfNotExists)
                   && Elements.Is(create.Elements)
                   && IncludeColumns.Is(create.IncludeColumns)
                   && WhereClause.Check(create.WhereClause);
        }

        public override WitSqlStatementCreateIndex Clone()
        {
            return new WitSqlStatementCreateIndex
            {
                Line = Line,
                Column = Column,
                IndexName = IndexName,
                TableName = TableName,
                IsUnique = IsUnique,
                IfNotExists = IfNotExists,
                Elements = Elements.Select(e => e.Clone()).ToList(),
                IncludeColumns = IncludeColumns?.ToList(),
                WhereClause = (WitSqlExpression?)WhereClause?.Clone()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string IndexName { get; init; }

        [ToString]
        public required string TableName { get; init; }

        public bool IsUnique { get; init; }

        public bool IfNotExists { get; init; }

        /// <summary>
        /// Index elements (columns or expressions).
        /// </summary>
        public required IReadOnlyList<ClauseIndexElement> Elements { get; init; }

        /// <summary>
        /// Columns included in covering index (INCLUDE clause).
        /// </summary>
        public IReadOnlyList<string>? IncludeColumns { get; init; }

        /// <summary>
        /// WHERE condition for partial/filtered index.
        /// </summary>
        public WitSqlExpression? WhereClause { get; init; }

        #endregion
    }
}