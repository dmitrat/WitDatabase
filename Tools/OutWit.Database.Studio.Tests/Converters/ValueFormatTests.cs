using System.Globalization;
using NUnit.Framework;
using OutWit.Database.Studio.Converters;

namespace OutWit.Database.Studio.Tests.Converters;

/// <summary>
/// How a value is written on screen, and what it accepts back (WS-65, WS-66).
///
/// <para>
/// <b>The first case is a defect that shipped.</b> Until this stage <c>FormatForDisplay</c> returned
/// numbers and dates unchanged, with a comment saying the DataGrid would render them "culture-aware".
/// It does: on a ru-RU machine a DECIMAL was drawn as <c>4812,50</c> and a DATETIME as
/// <c>28.06.2026</c>, and neither can be pasted into a statement. Nothing in the suite could see it,
/// because every case ran under the developer's own en-US culture - which is the shape of defect the
/// phase-13 lesson is about: the instrument was cleaner than production.
/// </para>
/// </summary>
[TestFixture]
public class ValueFormatTests
{
    #region Fields

    private CultureInfo m_culture = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_culture = CultureInfo.CurrentCulture;
    }

    [TearDown]
    public void TearDown()
    {
        CultureInfo.CurrentCulture = m_culture;

        ValueFormat.Current = ValueFormat.Default;
    }

    #endregion

    #region The default, on a machine that is not English

    /// <summary>
    /// WS-65. The machine is Russian, the values are not: this is the case the whole setting exists
    /// for, and it is the one that was red before the fix.
    /// </summary>
    [Test]
    public void OnARussianMachineTheGridStillWritesADotAndIsoTest()
    {
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

        Assert.Multiple(() =>
        {
            Assert.That(Show(4812.50m), Is.EqualTo("4812.50"));
            Assert.That(Show(1234567.25d), Is.EqualTo("1234567.25"));
            Assert.That(Show(new DateTime(2026, 6, 28, 14, 2, 0)), Is.EqualTo("2026-06-28 14:02:00"));
            Assert.That(Show(new DateOnly(2026, 6, 28)), Is.EqualTo("2026-06-28"));
        });
    }

    /// <summary>
    /// And the point of it, stated as the thing the user actually does: what the grid shows goes into
    /// a statement unchanged. Round-tripped through the parser rather than eyeballed.
    /// </summary>
    [Test]
    public void WhatTheGridShowsGoesBackIntoAStatementUnchangedTest()
    {
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

        var shown = Show(4812.50m);

        Assert.That(SqlValueParser.Parse(shown, typeof(decimal)), Is.EqualTo(4812.50m));
    }

    /// <summary>
    /// CONTROL: the setting is a real switch, not a decoration. Asking for the system's own format on
    /// the same machine produces the comma - so the case above is measuring the setting and not the
    /// absence of one.
    /// </summary>
    [Test]
    public void ChoosingTheSystemFormatGivesTheSystemsOwnSeparatorTest()
    {
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

        var system = new ValueFormat(ValueFormat.SYSTEM, ValueFormat.SYSTEM, ValueFormat.BINARY_SIZE);

        Assert.Multiple(() =>
        {
            Assert.That(SqlValueFormatter.FormatForDisplay(4812.50m, system)?.ToString(), Is.EqualTo("4812,50"));
            Assert.That(SqlValueFormatter.FormatForDisplay(new DateOnly(2026, 6, 28), system)?.ToString(),
                Is.EqualTo("28.06.2026"));
        });
    }

    /// <summary>A date at midnight is a date, not a date and four zeroes.</summary>
    [Test]
    public void ADateWithNoTimeIsWrittenWithoutOneTest()
    {
        Assert.That(Show(new DateTime(2026, 6, 28)), Is.EqualTo("2026-06-28"));
    }

    [Test]
    public void NullAndBooleansAreUnchangedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Show(null), Is.EqualTo(SqlValueFormatter.NULL_DISPLAY_TEXT));
            Assert.That(Show(DBNull.Value), Is.EqualTo(SqlValueFormatter.NULL_DISPLAY_TEXT));
            Assert.That(Show(true), Is.EqualTo("true"));
            Assert.That(Show(false), Is.EqualTo("false"));
        });
    }

    /// <summary>
    /// An integer is left to render itself, and that is deliberate rather than an omission: the default
    /// numeric format inserts no group separator in any culture, so there is nothing here to get wrong.
    /// Measured - this is the case that made the first version of the language test powerless.
    /// </summary>
    [Test]
    public void AnIntegerHasNothingToGetWrongTest()
    {
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

        Assert.That(1234567L.ToString(CultureInfo.CurrentCulture), Is.EqualTo("1234567"));
    }

    #endregion

    #region Binary

    [TestCase(ValueFormat.BINARY_SIZE, "(4 bytes)")]
    [TestCase(ValueFormat.BINARY_HEX, "0xDEADBEEF")]
    [TestCase(ValueFormat.BINARY_BASE64, "3q2+7w==")]
    public void ABlobIsShownTheChosenWayTest(string binary, string expected)
    {
        var format = ValueFormat.Default with { Binary = binary };

        Assert.That(SqlValueFormatter.FormatForDisplay(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, format)?.ToString(),
            Is.EqualTo(expected));
    }

    [Test]
    public void AnEmptyBlobSaysSoTest()
    {
        Assert.That(Show(Array.Empty<byte>()), Is.EqualTo(SqlValueFormatter.EMPTY_BLOB_TEXT));
    }

    #endregion

    #region Input is more tolerant than output (WS-66)

    [TestCase("4812.50", 4812.50)]
    [TestCase("4812,50", 4812.50)]
    [TestCase("1,234.56", 1234.56)]
    [TestCase("1.234,56", 1234.56)]
    [TestCase("1 234.56", 1234.56)]
    [TestCase("-7,5", -7.5)]
    public void ANumberIsAcceptedInEitherNotationTest(string typed, double expected)
    {
        Assert.That(SqlValueParser.Parse(typed, typeof(decimal)), Is.EqualTo((decimal)expected));
    }

    /// <summary>
    /// The ambiguity named out loud: a lone comma is a DECIMAL separator, so <c>1,234</c> is 1.234 and
    /// not one thousand two hundred and thirty-four. That is what a Russian keyboard means by it, and
    /// the editor shows the parsed value back so the reading is visible.
    /// </summary>
    [Test]
    public void ALoneCommaIsADecimalSeparatorTest()
    {
        Assert.That(SqlValueParser.Parse("1,234", typeof(decimal)), Is.EqualTo(1.234m));
    }

    [TestCase("2026-06-28")]
    [TestCase("2026-06-28 14:02")]
    [TestCase("28.06.2026")]
    [TestCase("28/06/2026")]
    public void ADateIsAcceptedInTheUsualFormsTest(string typed)
    {
        var parsed = (DateTime)SqlValueParser.Parse(typed, typeof(DateTime))!;

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Year, Is.EqualTo(2026));
            Assert.That(parsed.Month, Is.EqualTo(6));
            Assert.That(parsed.Day, Is.EqualTo(28));
        });
    }

    /// <summary>
    /// What makes the tolerance safe: the editor can say what the text became, in the one form Studio
    /// writes. An ambiguous date is then something the user sees rather than something they discover.
    /// </summary>
    [Test]
    public void TheParsedValueCanBeShownBackInCanonicalFormTest()
    {
        var parsed = SqlValueParser.Parse("28.06.2026", typeof(DateTime));

        Assert.That(SqlValueParser.Canonical(parsed), Is.EqualTo("2026-06-28"));
    }

    /// <summary>
    /// And the canonical form does not follow the machine either - it is what goes into a statement.
    /// </summary>
    [Test]
    public void TheCanonicalFormIgnoresTheMachinesLocaleTest()
    {
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        ValueFormat.Current = new ValueFormat(ValueFormat.SYSTEM, ValueFormat.SYSTEM, ValueFormat.BINARY_SIZE);

        Assert.That(SqlValueParser.Canonical(4812.50m), Is.EqualTo("4812.50"));
    }

    /// <summary>
    /// Text is text: a string column holding "1,234" keeps its comma. The tolerance is about numbers
    /// and dates, and a rule that reached strings would edit the user's data.
    /// </summary>
    [Test]
    public void TextIsNotNormalisedTest()
    {
        Assert.That(SqlValueParser.Parse("1,234", typeof(string)), Is.EqualTo("1,234"));
    }

    #endregion

    #region Tools

    private static string? Show(object? value)
    {
        return SqlValueFormatter.FormatForDisplay(value, ValueFormat.Default)?.ToString();
    }

    #endregion
}
