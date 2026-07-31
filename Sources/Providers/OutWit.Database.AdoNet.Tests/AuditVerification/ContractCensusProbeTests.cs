using System.Data.Common;
using System.Reflection;
using System.Text;

namespace OutWit.Database.AdoNet.Tests.AuditVerification;

/// <summary>
/// Phase 6 instrument A - a member-by-member census of what the ADO.NET base types promise and what this
/// provider actually does about each one.
/// </summary>
/// <remarks>
/// <para>
/// The phase's whole premise is that "the application must not notice" fails first in the surface the
/// application holds, and that the audit's own harness missed it by holding <c>WitDbConnection</c> rather
/// than <c>DbConnection</c>. So the first instrument does not test behaviour at all: it asks, for every
/// virtual or abstract member the base types declare, whether this provider <b>overrides</b> it,
/// <b>shadows</b> it, or leaves it <b>inherited</b>.
/// </para>
/// <para>
/// <b>Shadowed is the dangerous middle</b>, and it is the reason this census exists. A member declared
/// <c>public void Save(string)</c> instead of <c>public override void Save(string)</c> passes every test
/// written against the concrete type and throws <c>NotSupportedException</c> for a consumer holding the
/// base type - which is every consumer written against the contract, including EF Core. Reflection sees
/// the difference; a behavioural test only sees it if it remembered to hold the base type.
/// </para>
/// <para>
/// Inherited is not automatically a defect - most of the base implementations are perfectly good, and a
/// provider is not required to override everything. It is reported so that the ones that matter can be
/// judged rather than assumed.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class ContractCensusProbeTests
{
    #region Types

    private enum Disposition
    {
        /// <summary>Declared with <c>override</c> - the contract reaches this provider's code.</summary>
        Overridden,

        /// <summary>Declared with <c>new</c>, or simply re-declared - invisible through the base type.</summary>
        Shadowed,

        /// <summary>Not declared here; the base class implementation stands.</summary>
        Inherited
    }

    private sealed record Member(string Type, string Signature, Disposition Disposition);

    #endregion

    #region Census

    /// <summary>
    /// The contract surface this phase is about. Each pair is a base type a consumer may hold and the
    /// concrete type this provider hands them.
    /// </summary>
    private static readonly (Type Base, Type Concrete)[] SURFACE =
    [
        (typeof(DbConnection), typeof(WitDbConnection)),
        (typeof(DbCommand), typeof(WitDbCommand)),
        (typeof(DbTransaction), typeof(WitDbTransaction)),
        (typeof(DbDataReader), typeof(WitDbDataReader)),
        (typeof(DbParameter), typeof(WitDbParameter)),
        (typeof(DbParameterCollection), typeof(WitDbParameterCollection)),
        (typeof(DbProviderFactory), typeof(WitDbProviderFactory)),
        (typeof(DbCommandBuilder), typeof(WitDbCommandBuilder)),
        (typeof(DbDataAdapter), typeof(WitDbDataAdapter)),
        (typeof(DbConnectionStringBuilder), typeof(WitDbConnectionStringBuilder))
    ];

    /// <summary>
    /// Probe: the census itself. Reported in full, because the work order for this phase comes out of it.
    /// </summary>
    [Test]
    public void ProbeTheContractCensusTest()
    {
        var members = SURFACE.SelectMany(pair => Census(pair.Base, pair.Concrete)).ToList();

        foreach (var group in members.GroupBy(m => m.Type))
        {
            var counts = group.GroupBy(m => m.Disposition)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}={g.Count()}");

            TestContext.Out.WriteLine($"CENSUS  {group.Key,-24} {string.Join("  ", counts)}");
        }

        var shadowed = members.Where(m => m.Disposition == Disposition.Shadowed).ToList();

        TestContext.Out.WriteLine("");
        foreach (var m in shadowed)
            TestContext.Out.WriteLine($"CENSUS  SHADOWED  {m.Type}.{m.Signature}");

        // Inherited is not a defect by itself - a provider is not required to override everything, and
        // most base implementations are right. It is listed by name so that the ones that matter can be
        // judged rather than assumed, which is how EnlistTransaction and ConnectionTimeout were found.
        TestContext.Out.WriteLine("");
        foreach (var m in members.Where(m => m.Disposition == Disposition.Inherited))
            TestContext.Out.WriteLine($"CENSUS  inherited  {m.Type}.{m.Signature}");

        Assert.That(members, Is.Not.Empty, "the census found no members at all - reflection is wrong");
    }

    /// <summary>
    /// Probe: nothing on the contract surface may be shadowed. This is the phase's acceptance criterion
    /// expressed as a single assertion, and it is the one that turns green when the phase is done.
    /// </summary>
    [Test]
    public void ProbeNoContractMemberIsShadowedTest()
    {
        var shadowed = SURFACE
            .SelectMany(pair => Census(pair.Base, pair.Concrete))
            .Where(m => m.Disposition == Disposition.Shadowed)
            .Select(m => $"{m.Type}.{m.Signature}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var report = new StringBuilder();
        foreach (var s in shadowed)
            report.AppendLine($"  {s}");

        // A shadowed member is the failure mode this phase exists for: it passes on the concrete type and
        // throws through the base type, so every test written the way the audit wrote them says it works.
        Assert.That(shadowed, Is.Empty,
            $"members reachable through a base type are shadowed rather than overridden:\n{report}");
    }

    /// <summary>
    /// Probe: what the census's <c>WitDbParameter</c> finding costs a consumer. <b>In no audit</b> - the
    /// recorded contract gaps were all on <c>DbTransaction</c>.
    /// </summary>
    /// <remarks>
    /// <c>DbParameter.Precision</c> and <c>Scale</c> are virtual on the base type, and this provider
    /// declares its own without <c>override</c>. A consumer holding a <c>DbParameter</c> - which is what
    /// <c>DbCommand.CreateParameter()</c> returns, and what every framework built on the contract holds -
    /// therefore writes into the base class's storage, and the value never reaches the provider.
    /// </remarks>
    [Test]
    public void ProbePrecisionAndScaleSetThroughTheBaseTypeAreLostTest()
    {
        using var command = new WitDbCommand();

        // Exactly what a consumer written against the contract does: the factory hands back a
        // DbParameter, and the consumer never sees the concrete type.
        DbParameter parameter = command.CreateParameter();
        parameter.Precision = 5;
        parameter.Scale = 2;

        var concrete = (WitDbParameter)parameter;

        TestContext.Out.WriteLine(
            $"PROBE  set through DbParameter: Precision=5, Scale=2  ->  the provider sees "
            + $"Precision={concrete.Precision}, Scale={concrete.Scale}");

        // INVERTED BY THE FIX, and the inversion is the proof it landed. This used to read 0 and 0 and
        // pass: the provider declared its own Precision and Scale without override, so a consumer
        // holding a DbParameter wrote into the base class's storage and the value never arrived.
        Assert.Multiple(() =>
        {
            Assert.That(concrete.Precision, Is.EqualTo(5),
                "Precision set through the base type did not reach the provider");
            Assert.That(concrete.Scale, Is.EqualTo(2),
                "Scale set through the base type did not reach the provider");
        });
    }

    /// <summary>
    /// Probe: <see cref="DbCommandBuilder.QuoteIdentifier"/> on a builder that already knows its quote
    /// characters. The census lists it as inherited, and inherited means the base implementation - which
    /// for this member is "throw".
    /// </summary>
    [Test]
    public void ProbeTheCommandBuilderCanQuoteAnIdentifierTest()
    {
        using var builder = new WitDbCommandBuilder();

        TestContext.Out.WriteLine(
            $"PROBE  the builder's quote characters  ->  prefix={builder.QuotePrefix}, suffix={builder.QuoteSuffix}");

        Assert.Multiple(() =>
        {
            Assert.That(builder.QuotePrefix, Is.EqualTo("\""), "the builder is configured with them");

            Assert.That(builder.QuoteIdentifier("Order"), Is.EqualTo("\"Order\""),
                "a builder that knows its quote characters must be able to apply them");

            Assert.That(builder.UnquoteIdentifier("\"Order\""), Is.EqualTo("Order"),
                "and take them off again");

            Assert.That(builder.QuoteIdentifier("say \"what\""), Is.EqualTo("\"say \"\"what\"\"\""),
                "a quote character inside the identifier is doubled, not left to close it early");

            Assert.That(builder.UnquoteIdentifier("\"say \"\"what\"\"\""), Is.EqualTo("say \"what\""),
                "and undoubled on the way back");

            Assert.That(builder.UnquoteIdentifier("Order"), Is.EqualTo("Order"),
                "an identifier that was not quoted comes back as it went in");
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// Classifies every virtual or abstract member the base type declares. Only members a consumer can
    /// reach through the base type are interesting, so the census walks the BASE type's surface and asks
    /// what the concrete type did about each one - not the other way round.
    /// </summary>
    private static IEnumerable<Member> Census(Type baseType, Type concrete)
    {
        const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (var method in baseType.GetMethods(FLAGS))
        {
            if (method.IsSpecialName || !(method.IsVirtual || method.IsAbstract))
                continue;

            yield return new Member(concrete.Name, Describe(method), Classify(method, concrete));
        }

        foreach (var property in baseType.GetProperties(FLAGS))
        {
            var accessor = property.GetGetMethod() ?? property.GetSetMethod();
            if (accessor == null || !(accessor.IsVirtual || accessor.IsAbstract))
                continue;

            yield return new Member(concrete.Name, property.Name, Classify(accessor, concrete, property));
        }
    }

    private static Disposition Classify(MethodInfo baseMethod, Type concrete, PropertyInfo? property = null)
    {
        const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        if (property != null)
        {
            // Matched on the index parameters as well as the name: DbDataReader declares this[int] and
            // this[string], and asking for "Item" by name alone is ambiguous.
            var indexTypes = property.GetIndexParameters().Select(p => p.ParameterType).ToArray();

            var declared = concrete.GetProperties(FLAGS).FirstOrDefault(p =>
                p.Name == property.Name
                && p.GetIndexParameters().Select(i => i.ParameterType).SequenceEqual(indexTypes));

            if (declared == null)
                return Disposition.Inherited;

            var declaredAccessor = declared.GetGetMethod() ?? declared.GetSetMethod();

            return declaredAccessor != null && Overrides(declaredAccessor, baseMethod)
                ? Disposition.Overridden
                : Disposition.Shadowed;
        }

        var types = baseMethod.GetParameters().Select(p => p.ParameterType).ToArray();
        var candidate = concrete.GetMethod(baseMethod.Name, FLAGS, binder: null, types, modifiers: null);

        if (candidate == null)
            return Disposition.Inherited;

        return Overrides(candidate, baseMethod) ? Disposition.Overridden : Disposition.Shadowed;
    }

    /// <summary>
    /// True when the concrete method is an override of the base one. An override shares the base
    /// definition; a <c>new</c> member is its own base definition, which is exactly the distinction a
    /// consumer holding the base type feels.
    /// </summary>
    private static bool Overrides(MethodInfo candidate, MethodInfo baseMethod) =>
        candidate.IsVirtual && candidate.GetBaseDefinition().DeclaringType == baseMethod.GetBaseDefinition().DeclaringType;

    private static string Describe(MethodInfo method)
    {
        var parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
        return $"{method.Name}({parameters})";
    }

    #endregion
}
