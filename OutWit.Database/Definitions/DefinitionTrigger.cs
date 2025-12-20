using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// Defines a database trigger.
    /// </summary>
    public sealed class DefinitionTrigger : ModelBase
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
        public required string Name { get; init; }

        /// <summary>
        /// Gets the table this trigger is attached to.
        /// </summary>
        public required string TableName { get; init; }

        /// <summary>
        /// Gets when the trigger fires relative to the operation.
        /// </summary>
        public required TriggerTime Time { get; init; }

        /// <summary>
        /// Gets the event that fires the trigger.
        /// </summary>
        public required TriggerEvent Event { get; init; }

        /// <summary>
        /// Gets the columns for UPDATE OF triggers. Null means all columns.
        /// </summary>
        public IReadOnlyList<string>? UpdateColumns { get; init; }

        /// <summary>
        /// Gets whether this is a FOR EACH ROW trigger.
        /// </summary>
        public bool ForEachRow { get; init; }

        /// <summary>
        /// Gets the optional WHEN condition (SQL expression).
        /// </summary>
        public string? WhenCondition { get; init; }

        /// <summary>
        /// Gets the trigger body (SQL statements).
        /// </summary>
        public required string Body { get; init; }

        #endregion
    }

    /// <summary>
    /// The event that fires a trigger.
    /// </summary>
    public enum TriggerEvent
    {
        Insert,
        Update,
        Delete
    }

    /// <summary>
    /// When a trigger fires relative to the operation.
    /// </summary>
    public enum TriggerTime
    {
        Before,
        After,
        InsteadOf
    }
}