using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementCreateTrigger : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementCreateTrigger(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementCreateTrigger create)
                return false;

            return base.Is(create, tolerance)
                   && TriggerName.Is(create.TriggerName)
                   && IfNotExists.Is(create.IfNotExists)
                   && Time.Is(create.Time)
                   && Event.Is(create.Event)
                   && UpdateColumns.Is(create.UpdateColumns)
                   && TableName.Is(create.TableName)
                   && ForEachRow.Is(create.ForEachRow)
                   && WhenCondition.Check(create.WhenCondition)
                   && Body.Is(create.Body)
                   && BodyWitSql.Is(create.BodyWitSql);
        }

        public override WitSqlStatementCreateTrigger Clone()
        {
            return new WitSqlStatementCreateTrigger
            {
                Line = Line,
                Column = Column,
                TriggerName = TriggerName,
                IfNotExists = IfNotExists,
                Time = Time,
                Event = Event,
                UpdateColumns = UpdateColumns?.ToList(),
                TableName = TableName,
                ForEachRow = ForEachRow,
                WhenCondition = (WitSqlExpression?)WhenCondition?.Clone(),
                Body = Body.Select(statement => (WitSqlStatement)statement.Clone()).ToList(),
                BodyWitSql = BodyWitSql
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string TriggerName { get; init; }
        public bool IfNotExists { get; init; }

        [ToString]
        public required TriggerTimingType Time { get; init; }

        [ToString]
        public required TriggerEventType Event { get; init; }
        public IReadOnlyList<string>? UpdateColumns { get; init; }

        [ToString]
        public required string TableName { get; init; }
        public bool ForEachRow { get; init; }
        public WitSqlExpression? WhenCondition { get; init; }
        public required IReadOnlyList<WitSqlStatement> Body { get; init; }

        /// <summary>
        /// Original WitSql text of the trigger body for storage.
        /// </summary>
        public string? BodyWitSql { get; init; }

        #endregion
    }
}