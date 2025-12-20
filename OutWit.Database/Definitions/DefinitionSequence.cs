using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// Represents a database sequence for generating sequential values.
    /// </summary>
    public sealed class DefinitionSequence : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if(modelBase is not DefinitionSequence other)
                return false;

            return Name.Is(other.Name)
                   && StartWith.Is(other.StartWith)
                   && IncrementBy.Is(other.IncrementBy)
                   && CurrentValue.Is(other.CurrentValue)
                   && MinValue.Is(other.MinValue)
                   && MaxValue.Is(other.MaxValue)
                   && Cycle.Is(other.Cycle);
        }

        public override DefinitionSequence Clone()
        {
            return new DefinitionSequence
            {
                Name = Name,
                StartWith = StartWith,
                IncrementBy = IncrementBy,
                CurrentValue = CurrentValue,
                MinValue = MinValue,
                MaxValue = MaxValue,
                Cycle = Cycle
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string Name { get; init; }
        public long StartWith { get; init; } = 1;
        public long IncrementBy { get; init; } = 1;
        public long CurrentValue { get; set; }
        public long? MinValue { get; init; }
        public long? MaxValue { get; init; }
        public bool Cycle { get; init; } = false;

        #endregion


    }
}
