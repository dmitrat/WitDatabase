using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Schema
{
    /// <summary>
    /// One declared parameter of a function or a procedure, as the parser saw it.
    /// </summary>
    /// <remarks>
    /// No direction. <c>OUT</c> and <c>INOUT</c> need a way to hand a value back to the caller, and
    /// this engine's ADO surface has none - <c>WitDbDataReader.NextResult</c> is hard-coded false and
    /// there is no output-parameter protocol. A field for a direction the engine cannot honour would
    /// be a promise the grammar makes and nothing keeps, which is the "accepted, not enforced" class
    /// phase 7 spent itself closing.
    /// </remarks>
    [MemoryPackable]
    public sealed partial class WitSqlRoutineParameter : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlRoutineParameter parameter)
                return false;

            return Name.Is(parameter.Name)
                   && DataType.Check(parameter.DataType);
        }

        public override WitSqlRoutineParameter Clone()
        {
            return new WitSqlRoutineParameter
            {
                Name = Name,
                DataType = DataType.Clone()
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// The parameter name, as written.
        /// </summary>
        [ToString]
        public required string Name { get; init; }

        /// <summary>
        /// The declared type.
        /// </summary>
        [ToString]
        public required WitSqlDataType DataType { get; init; }

        #endregion
    }
}
