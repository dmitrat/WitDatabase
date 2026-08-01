using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema;
using OutWit.Database.Parser.Schema.TableConstraints;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementCreateTable : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementCreateTable(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementCreateTable create)
                return false;

            return base.Is(create, tolerance) 
                   && TableName.Is(create.TableName)
                   && IfNotExists.Is(create.IfNotExists)
                   && Columns.Is(create.Columns)
                   && Constraints.Is(create.Constraints);
        }

        public override WitSqlStatementCreateTable Clone()
        {
            return new WitSqlStatementCreateTable
            {
                Line = Line,
                Column = Column,
                TableName = TableName,
                IfNotExists = IfNotExists,
                Columns = Columns.Select(column => column.Clone()).ToList(),
                Constraints = Constraints?.Select(constraint => (TableConstraint)constraint.Clone()).ToList()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string TableName { get; init; }
        public bool IfNotExists { get; init; }
        public required IReadOnlyList<WitSqlColumn> Columns { get; init; }
        public IReadOnlyList<TableConstraint>? Constraints { get; init; }

        #endregion
    }
}