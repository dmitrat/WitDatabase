using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Types;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// One parameter of a function or a procedure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shared by both routine kinds because <c>INFORMATION_SCHEMA.PARAMETERS</c> reports them from
    /// one view, and a parameter is the same thing in both: a name, a type and a position. Splitting
    /// it would mean two records to keep in step for no difference in content.
    /// </para>
    /// <para>
    /// <b>Direction is not stored, because only one direction exists.</b> <c>OUT</c> and <c>INOUT</c>
    /// parameters need a way to hand a value back to the caller, and the ADO surface here has none -
    /// <c>WitDbDataReader.NextResult</c> is hard-coded <c>false</c> and there is no output-parameter
    /// protocol. Adding a field for a direction the engine cannot honour would be a catalog column
    /// that lies. <c>PARAMETERS.PARAMETER_MODE</c> reports the constant <c>IN</c>, which is true.
    /// </para>
    /// </remarks>
    [MemoryPackable]
    public sealed partial class DefinitionRoutineParameter : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not DefinitionRoutineParameter other)
                return false;

            return Name.Is(other.Name)
                   && Type.Is(other.Type)
                   && MaxLength.Is(other.MaxLength)
                   && Precision.Is(other.Precision)
                   && Scale.Is(other.Scale);
        }

        public override DefinitionRoutineParameter Clone()
        {
            return new DefinitionRoutineParameter
            {
                Name = Name,
                Type = Type,
                MaxLength = MaxLength,
                Precision = Precision,
                Scale = Scale
            };
        }

        #endregion

        #region Functions

        public override string ToString() => $"{Name} {Type}";

        #endregion

        #region Properties

        /// <summary>
        /// The parameter name, as written in the routine's declaration.
        /// </summary>
        [MemoryPackOrder(0)]
        public required string Name { get; init; }

        /// <summary>
        /// The declared type.
        /// </summary>
        [MemoryPackOrder(1)]
        public required WitDataType Type { get; init; }

        /// <summary>
        /// The declared length for a sized type, or null.
        /// </summary>
        /// <remarks>
        /// Carried for the same reason <see cref="DefinitionColumn.MaxLength"/> is: the parser has
        /// always known it, and a catalog that drops it reports a routine signature that is not the
        /// one that was declared. Phase 7 found every column in the database with these three fields
        /// null because nothing copied them across.
        /// </remarks>
        [MemoryPackOrder(2)]
        public int? MaxLength { get; init; }

        /// <summary>
        /// The declared precision for a numeric type, or null.
        /// </summary>
        [MemoryPackOrder(3)]
        public int? Precision { get; init; }

        /// <summary>
        /// The declared scale for a numeric type, or null.
        /// </summary>
        [MemoryPackOrder(4)]
        public int? Scale { get; init; }

        #endregion
    }
}
