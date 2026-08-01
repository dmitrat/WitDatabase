using System.Reflection;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Nodes;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.Serialization;

/// <summary>
/// Guards the parts of the storage contract that a round-trip test cannot see.
/// </summary>
/// <remarks>
/// <para>
/// The round-trip test proves that what this build writes, this build reads. It says nothing about
/// what <b>a later build</b> reads, and that is the whole risk of moving the catalog from text to a
/// tagged binary format: a union tag is a number burned into every database file that has ever been
/// written. Renumber one and old files silently deserialize into the wrong node type - a corruption
/// with no exception at the point it happens.
/// </para>
/// <para>
/// Two hazards, one test each: the tag table drifting (pinned below, exhaustively), and a new AST
/// type being added without markup (a census, so the gap is found at build time rather than by a
/// user whose <c>CHECK</c> constraint stopped saving).
/// </para>
/// </remarks>
[TestFixture]
[Category("Grammar")]
public class AstMemoryPackContractTests
{
    #region Tag stability

    /// <summary>
    /// The persisted tag table, pinned. A change here is a <b>file format change</b> and must be
    /// treated as one: appending a type is safe, renumbering or reusing a tag is not.
    /// </summary>
    /// <remarks>
    /// Written out in full rather than derived, deliberately. Deriving it from the attributes would
    /// make the test agree with whatever the code says, which is exactly the drift it exists to
    /// catch - the same reason the reserved-word list must come from the grammar and not from a
    /// second hand-kept copy of it.
    /// </remarks>
    private static readonly (Type Base, int Tag, string Type)[] PINNED_TAGS =
    [
        (typeof(WitSqlNode), 0, "WitSqlExpression"),
        (typeof(WitSqlNode), 1, "WitSqlStatement"),

        (typeof(WitSqlExpression), 0, "WitSqlExpressionBetween"),
        (typeof(WitSqlExpression), 1, "WitSqlExpressionBinary"),
        (typeof(WitSqlExpression), 2, "WitSqlExpressionCase"),
        (typeof(WitSqlExpression), 3, "WitSqlExpressionCast"),
        (typeof(WitSqlExpression), 4, "WitSqlExpressionCollate"),
        (typeof(WitSqlExpression), 5, "WitSqlExpressionColumnRef"),
        (typeof(WitSqlExpression), 6, "WitSqlExpressionExists"),
        (typeof(WitSqlExpression), 7, "WitSqlExpressionFunctionCall"),
        (typeof(WitSqlExpression), 8, "WitSqlExpressionGlob"),
        (typeof(WitSqlExpression), 9, "WitSqlExpressionIif"),
        (typeof(WitSqlExpression), 10, "WitSqlExpressionIn"),
        (typeof(WitSqlExpression), 11, "WitSqlExpressionIsNull"),
        (typeof(WitSqlExpression), 12, "WitSqlExpressionLike"),
        (typeof(WitSqlExpression), 13, "WitSqlExpressionLiteral"),
        (typeof(WitSqlExpression), 14, "WitSqlExpressionOrderByColumnIndex"),
        (typeof(WitSqlExpression), 15, "WitSqlExpressionParameter"),
        (typeof(WitSqlExpression), 16, "WitSqlExpressionQuantified"),
        (typeof(WitSqlExpression), 17, "WitSqlExpressionSubquery"),
        (typeof(WitSqlExpression), 18, "WitSqlExpressionUnary"),

        (typeof(WitSqlStatement), 0, "WitSqlStatementAlterSequence"),
        (typeof(WitSqlStatement), 1, "WitSqlStatementAlterTable"),
        (typeof(WitSqlStatement), 2, "WitSqlStatementBeginTransaction"),
        (typeof(WitSqlStatement), 3, "WitSqlStatementCommit"),
        (typeof(WitSqlStatement), 4, "WitSqlStatementCreateIndex"),
        (typeof(WitSqlStatement), 5, "WitSqlStatementCreateSequence"),
        (typeof(WitSqlStatement), 6, "WitSqlStatementCreateTable"),
        (typeof(WitSqlStatement), 7, "WitSqlStatementCreateTrigger"),
        (typeof(WitSqlStatement), 8, "WitSqlStatementCreateView"),
        (typeof(WitSqlStatement), 9, "WitSqlStatementDelete"),
        (typeof(WitSqlStatement), 10, "WitSqlStatementDropIndex"),
        (typeof(WitSqlStatement), 11, "WitSqlStatementDropSequence"),
        (typeof(WitSqlStatement), 12, "WitSqlStatementDropTable"),
        (typeof(WitSqlStatement), 13, "WitSqlStatementDropTrigger"),
        (typeof(WitSqlStatement), 14, "WitSqlStatementDropView"),
        (typeof(WitSqlStatement), 15, "WitSqlStatementExplain"),
        (typeof(WitSqlStatement), 16, "WitSqlStatementInsert"),
        (typeof(WitSqlStatement), 17, "WitSqlStatementMerge"),
        (typeof(WitSqlStatement), 18, "WitSqlStatementReleaseSavepoint"),
        (typeof(WitSqlStatement), 19, "WitSqlStatementRollback"),
        (typeof(WitSqlStatement), 20, "WitSqlStatementSavepoint"),
        (typeof(WitSqlStatement), 21, "WitSqlStatementSelect"),
        (typeof(WitSqlStatement), 22, "WitSqlStatementSetTransaction"),
        (typeof(WitSqlStatement), 23, "WitSqlStatementSignal"),
        (typeof(WitSqlStatement), 24, "WitSqlStatementTruncate"),
        (typeof(WitSqlStatement), 25, "WitSqlStatementUpdate"),
    ];

    [Test]
    public void UnionTagsHaveNotMovedTest()
    {
        var actual = PINNED_TAGS
            .Select(pin => pin.Base)
            .Distinct()
            .SelectMany(TagsOf)
            .ToDictionary(entry => (entry.Base, entry.Tag), entry => entry.Type);

        var drifted = new List<string>();

        foreach (var (baseType, tag, expected) in PINNED_TAGS)
        {
            if (!actual.TryGetValue((baseType, tag), out var found))
                drifted.Add($"{baseType.Name} tag {tag} is gone; it used to be {expected}");
            else if (found.Name != expected)
                drifted.Add($"{baseType.Name} tag {tag} was {expected} and is now {found.Name}");
        }

        var added = actual
            .Where(entry => !PINNED_TAGS.Any(pin => pin.Base == entry.Key.Base && pin.Tag == entry.Key.Tag))
            .Select(entry => $"{entry.Key.Base.Name} tag {entry.Key.Tag} = {entry.Value} is new")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(drifted, Is.Empty,
                "A union tag is written into every database file. Changing one makes existing files " +
                "deserialize into a different node type, silently. Append instead:" +
                $"{Environment.NewLine}{string.Join(Environment.NewLine, drifted)}");

            // Additions are legal, but they must be recorded here in the same commit that makes
            // them, or the pin stops covering the part of the table that is newest and least tested.
            Assert.That(added, Is.Empty,
                $"New union members are not yet pinned. Add them to {nameof(PINNED_TAGS)}:" +
                $"{Environment.NewLine}{string.Join(Environment.NewLine, added)}");
        });
    }

    #endregion

    #region Coverage of the AST

    /// <summary>
    /// Every concrete AST node must be storable. A type added without markup is the drift this
    /// format's discipline is exposed to, and it would fail at runtime the first time a user
    /// declared the construct - so it is caught here instead.
    /// </summary>
    [Test]
    public void EveryAstNodeIsStorableTest()
    {
        var unmarked = AstTypes()
            .Where(type => type.GetCustomAttribute<MemoryPackableAttribute>() is null)
            .Select(type => type.FullName!)
            .OrderBy(name => name)
            .ToArray();

        Assert.That(unmarked, Is.Empty,
            $"{unmarked.Length} AST types cannot be written to the catalog:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, unmarked)}");
    }

    /// <summary>
    /// A concrete node that is MemoryPackable but reachable through no union is a node the base-typed
    /// property holding it cannot write - the failure MemoryPack raises at serialize time, which is
    /// loud but happens in front of a user rather than here.
    /// </summary>
    [Test]
    public void EveryConcreteNodeIsReachableThroughItsBaseTest()
    {
        var orphans = new List<string>();

        foreach (var type in AstTypes().Where(type => !type.IsAbstract))
        {
            for (var parent = type.BaseType; parent is not null && parent != typeof(ModelBase); parent = parent.BaseType)
            {
                if (!parent.IsAbstract)
                    continue;

                var members = parent.GetCustomAttributes<MemoryPackUnionAttribute>()
                    .Select(union => union.Type)
                    .ToArray();

                // The union lists the direct subtype, which may itself be an abstract union base.
                if (members.Length > 0 && !members.Any(member => member.IsAssignableFrom(type)))
                    orphans.Add($"{type.Name} is not reachable through {parent.Name}'s union");
            }
        }

        Assert.That(orphans, Is.Empty,
            $"{orphans.Count} nodes cannot be written through a base-typed property:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, orphans)}");
    }

    #endregion

    #region Determinism

    /// <summary>
    /// The same tree must produce the same bytes. The catalog compares stored records to decide
    /// whether a write is needed, so an unstable encoding would rewrite schema on every open.
    /// </summary>
    [Test]
    public void EncodingIsDeterministicTest()
    {
        var unstable = Grammar.GrammarCorpus.All
            .SelectMany(WitSql.Parse)
            .Where(statement => !MemoryPackSerializer.Serialize(statement)
                .SequenceEqual(MemoryPackSerializer.Serialize(statement)))
            .Select(statement => statement.GetType().Name)
            .Distinct()
            .ToArray();

        Assert.That(unstable, Is.Empty,
            $"{unstable.Length} statement types encode differently on two runs: " +
            string.Join(", ", unstable));
    }

    #endregion

    #region Literal payloads

    /// <summary>
    /// Each payload a literal can carry survives with its CLR type intact. Type identity is the
    /// point: a stored <c>1</c> that returns as <c>double</c> instead of <c>long</c> changes what
    /// comparisons against it mean.
    /// </summary>
    [TestCaseSource(nameof(LiteralPayloads))]
    public void LiteralPayloadKeepsItsTypeTest(object? value)
    {
        WitSqlExpression literal = new WitSqlExpressionLiteral { Type = LiteralType.String, Value = value };

        var back = MemoryPackSerializer.Deserialize<WitSqlExpression>(
            MemoryPackSerializer.Serialize(literal)) as WitSqlExpressionLiteral;

        Assert.That(back, Is.Not.Null);

        if (value is byte[] expected)
            Assert.That(back!.Value as byte[], Is.EqualTo(expected));
        else
            Assert.That(back!.Value, Is.EqualTo(value));

        Assert.That(back.Value?.GetType(), Is.EqualTo(value?.GetType()),
            "the payload's CLR type is part of what was stored");
    }

    private static object?[] LiteralPayloads() =>
    [
        null, 42L, -1L, long.MaxValue, 1.5d, -0.5d, "text", string.Empty, true, false,
        new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, Array.Empty<byte>(), 12.34m, decimal.MinValue
    ];

    /// <summary>
    /// A payload type with no tag is refused when written rather than written as something else.
    /// Silently coercing it is how a stored value changes type between releases.
    /// </summary>
    [Test]
    public void UntaggedLiteralPayloadIsRefusedTest()
    {
        WitSqlExpression literal = new WitSqlExpressionLiteral
        {
            Type = LiteralType.String,
            Value = new Uri("https://example.invalid")
        };

        Assert.That(() => MemoryPackSerializer.Serialize(literal),
            Throws.InstanceOf<NotSupportedException>()
                .Or.InnerException.InstanceOf<NotSupportedException>(),
            "an unknown literal payload must fail loudly at the point of writing");
    }

    #endregion

    #region Helpers

    private static IEnumerable<(Type Base, int Tag, Type Type)> TagsOf(Type baseType) =>
        baseType.GetCustomAttributes<MemoryPackUnionAttribute>()
            .Select(union => (baseType, (int)union.Tag, union.Type));

    /// <summary>Every AST type in the parser assembly, abstract bases included.</summary>
    private static IEnumerable<Type> AstTypes() =>
        typeof(WitSqlNode).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsPublic: true })
            .Where(type => typeof(ModelBase).IsAssignableFrom(type))
            .Where(type => type != typeof(ModelBase));

    #endregion
}
