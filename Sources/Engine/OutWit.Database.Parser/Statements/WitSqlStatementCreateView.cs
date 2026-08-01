using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementCreateView : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementCreateView(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementCreateView create)
                return false;

            return base.Is(create, tolerance)
                   && ViewName.Is(create.ViewName)
                   && IfNotExists.Is(create.IfNotExists)
                   && ColumnNames.Is(create.ColumnNames)
                   && Query.Is(create.Query, tolerance);
        }

        public override WitSqlStatementCreateView Clone()
        {
            return new WitSqlStatementCreateView
            {
                Line = Line,
                Column = Column,
                ViewName = ViewName,
                IfNotExists = IfNotExists,
                ColumnNames = ColumnNames?.ToList(),
                Query = Query.Clone()
            };
        }

        #endregion

        #region Prperties

        [ToString]
        public required string ViewName { get; init; }
        public bool IfNotExists { get; init; }
        public IReadOnlyList<string>? ColumnNames { get; init; }
        public required WitSqlStatementSelect Query { get; init; }

        #endregion
    }
}