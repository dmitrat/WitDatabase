using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// Defines a database trigger.
    /// </summary>
    [MemoryPackable]
    public sealed partial class DefinitionTrigger : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if(modelBase is not DefinitionTrigger other)
                return false;

            return Name.Is(other.Name)
                   && TableName.Is(other.TableName)
                   && Time.Is(other.Time)
                   && Event.Is(other.Event)
                   && UpdateColumns.Is(other.UpdateColumns)
                   && ForEachRow.Is(other.ForEachRow)
                   && WhenCondition.Is(other.WhenCondition)
                   && Body.Is(other.Body);
        }

        public override DefinitionTrigger Clone()
        {
            return new DefinitionTrigger
            {
                Name = Name,
                TableName = TableName,
                Time = Time,
                Event = Event,
                UpdateColumns = UpdateColumns?.ToArray(),
                ForEachRow = ForEachRow,
                WhenCondition = WhenCondition,
                Body = Body
            };
        }

        #endregion

        #region Functions

        public override string ToString()
        {
            return $"TRIGGER {Name} {Time} {Event} ON {TableName}";
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the trigger name.
        /// </summary>
        [MemoryPackOrder(0)]
        public required string Name { get; init; }

        /// <summary>
        /// Gets the table this trigger is attached to.
        /// </summary>
        [MemoryPackOrder(1)]
        public required string TableName { get; init; }

        /// <summary>
        /// Gets when the trigger fires relative to the operation.
        /// </summary>
        [MemoryPackOrder(2)]
        public required TriggerTime Time { get; init; }

        /// <summary>
        /// Gets the event that fires the trigger.
        /// </summary>
        [MemoryPackOrder(3)]
        public required TriggerEvent Event { get; init; }

        /// <summary>
        /// Gets the columns for UPDATE OF triggers. Null means all columns.
        /// </summary>
        [MemoryPackOrder(4)]
        public IReadOnlyList<string>? UpdateColumns { get; init; }

        /// <summary>
        /// Gets whether this is a FOR EACH ROW trigger.
        /// </summary>
        [MemoryPackOrder(5)]
        public bool ForEachRow { get; init; }

        /// <summary>
        /// Gets the optional WHEN condition (SQL expression).
        /// </summary>
        [MemoryPackOrder(6)]
        public string? WhenCondition { get; init; }

        /// <summary>
        /// Gets the trigger body (SQL statements).
        /// </summary>
        [MemoryPackOrder(7)]
        public required string Body { get; init; }

        #endregion
    }

    /// <summary>
    /// The event that fires a trigger.
    /// </summary>
    public enum TriggerEvent
    {
        /// <summary>
        /// Trigger fires on INSERT operations.
        /// </summary>
        Insert,

        /// <summary>
        /// Trigger fires on UPDATE operations.
        /// </summary>
        Update,

        /// <summary>
        /// Trigger fires on DELETE operations.
        /// </summary>
        Delete
    }

    /// <summary>
    /// When a trigger fires relative to the operation.
    /// </summary>
    public enum TriggerTime
    {
        /// <summary>
        /// Trigger fires before the operation (can modify or cancel).
        /// </summary>
        Before,

        /// <summary>
        /// Trigger fires after the operation completes.
        /// </summary>
        After,

        /// <summary>
        /// Trigger replaces the operation entirely.
        /// </summary>
        InsteadOf
    }
}