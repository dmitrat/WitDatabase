using MemoryPack;

namespace OutWit.Database.Parser.Serializers;

/// <summary>
/// Writes the boxed payload of <c>WitSqlExpressionLiteral.Value</c>, which is declared <c>object?</c>
/// because a literal carries whatever CLR type its <c>LiteralType</c> implies.
/// </summary>
/// <remarks>
/// <para>
/// MemoryPack refuses an <c>object</c> member outright (<c>MEMPACK018</c>), and it is right to: an
/// unconstrained <c>object</c> has no schema, so nothing downstream could bound what a database file
/// is allowed to deserialize into. This formatter supplies the missing schema as an explicit tag
/// byte, so the set of types a stored literal can produce is <b>closed and listed here</b>.
/// </para>
/// <para>
/// The tags are a persisted format: they are written into database files. <b>Never renumber one and
/// never reuse a retired one</b> - append only. An unknown tag is a corrupt or newer file and throws
/// rather than guessing, because guessing would silently change a stored value's type.
/// </para>
/// </remarks>
public sealed class LiteralValueFormatter : MemoryPackFormatter<object?>
{
    #region Attribute

    /// <summary>
    /// Applies <see cref="LiteralValueFormatter"/> to a member. MemoryPack's own
    /// <c>MemoryPackCustomFormatterAttribute&lt;T&gt;</c> is abstract, so the binding is declared here.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class ApplyAttribute : MemoryPackCustomFormatterAttribute<object?>
    {
        private static readonly LiteralValueFormatter FORMATTER = new();

        public override IMemoryPackFormatter<object?> GetFormatter() => FORMATTER;
    }

    #endregion

    #region Tags

    private const byte TAG_NULL = 0;
    private const byte TAG_INT64 = 1;
    private const byte TAG_DOUBLE = 2;
    private const byte TAG_STRING = 3;
    private const byte TAG_BOOLEAN = 4;
    private const byte TAG_BLOB = 5;
    private const byte TAG_DECIMAL = 6;

    #endregion

    #region Functions

    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteUnmanaged(TAG_NULL);
                return;

            case long int64:
                writer.WriteUnmanaged(TAG_INT64, int64);
                return;

            case double real:
                writer.WriteUnmanaged(TAG_DOUBLE, real);
                return;

            case string text:
                writer.WriteUnmanaged(TAG_STRING);
                writer.WriteString(text);
                return;

            case bool boolean:
                writer.WriteUnmanaged(TAG_BOOLEAN, boolean);
                return;

            case byte[] blob:
                writer.WriteUnmanaged(TAG_BLOB);
                writer.WriteUnmanagedArray(blob);
                return;

            case decimal exact:
                writer.WriteUnmanaged(TAG_DECIMAL, exact);
                return;

            default:
                // Loudly, because the alternative is a stored literal that reads back as something
                // else. A new literal payload type must be given a tag above, not tolerated here.
                throw new NotSupportedException(
                    $"A SQL literal cannot carry a value of type '{value.GetType().FullName}'. " +
                    $"Add an explicit tag to {nameof(LiteralValueFormatter)} if this type is real.");
        }
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref object? value)
    {
        reader.ReadUnmanaged(out byte tag);

        switch (tag)
        {
            case TAG_NULL:
                value = null;
                return;

            case TAG_INT64:
                reader.ReadUnmanaged(out long int64);
                value = int64;
                return;

            case TAG_DOUBLE:
                reader.ReadUnmanaged(out double real);
                value = real;
                return;

            case TAG_STRING:
                value = reader.ReadString();
                return;

            case TAG_BOOLEAN:
                reader.ReadUnmanaged(out bool boolean);
                value = boolean;
                return;

            case TAG_BLOB:
                value = reader.ReadUnmanagedArray<byte>();
                return;

            case TAG_DECIMAL:
                reader.ReadUnmanaged(out decimal exact);
                value = exact;
                return;

            default:
                throw new MemoryPackSerializationException(
                    $"Unknown literal payload tag {tag}. The file is corrupt, or it was written by a " +
                    $"newer version that defined a tag this one does not know.");
        }
    }

    #endregion
}
