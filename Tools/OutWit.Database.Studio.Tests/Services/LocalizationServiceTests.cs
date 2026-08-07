using System.Globalization;
using OutWit.Database.Studio.Services.Localization;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The interface language (WS-63, WS-64, WS-65).
///
/// <para>
/// Most of what is worth checking here is not "does a lookup return a string" - it is the three ways a
/// localisation quietly stops being one: a language that is a copy of the other, a term that was
/// translated when it should not have been, and a number that started following the thread culture.
/// Each has a case below, and each was run against a deliberately broken catalogue first.
/// </para>
/// </summary>
[TestFixture]
public class LocalizationServiceTests
{
    #region Constants

    /// <summary>
    /// Terms that must read the same in both languages (WS-64). A person searches the documentation,
    /// the issues and the sources for these; a translated one cannot be found.
    /// </summary>
    private static readonly string[] NOT_TRANSLATED =
    [
        "B-Tree", "LSM", "SSTable", "SQL", "CSV", "JSON", "MVCC", "WitDatabase", "AES", "ChaCha20"
    ];

    #endregion

    #region Fields

    private LocalizationService m_localization = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_localization = new LocalizationService();
    }

    #endregion

    #region The catalogues

    /// <summary>
    /// CONTROL, and it is the first one because every other case here is relational: "the two agree"
    /// proves nothing about two empty catalogues, and neither does "no term was translated".
    /// </summary>
    [Test]
    public void BothCataloguesAreEmbeddedAndCarryStringsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_localization.Texts("en"), Has.Count.GreaterThan(30),
                "CONTROL: the English catalogue is missing or nearly empty, so every comparison below is vacuous");
            Assert.That(m_localization.Texts("ru"), Has.Count.GreaterThan(30),
                "CONTROL: the Russian catalogue is missing or nearly empty");
        });
    }

    [Test]
    public void EveryKeyExistsInBothLanguagesTest()
    {
        var english = m_localization.Texts("en").Keys.ToHashSet();
        var russian = m_localization.Texts("ru").Keys.ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(russian.Except(english), Is.Empty, "keys the Russian catalogue has and the English does not");
            Assert.That(english.Except(russian), Is.Empty, "keys with no Russian translation");
        });
    }

    [Test]
    public void EveryPluralExistsInBothLanguagesTest()
    {
        var english = m_localization.Plurals("en").Keys.ToHashSet();
        var russian = m_localization.Plurals("ru").Keys.ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(english.Except(russian), Is.Empty, "plurals with no Russian forms");
            Assert.That(russian.Except(english), Is.Empty, "plurals the English catalogue does not have");
        });
    }

    /// <summary>
    /// THE NEGATIVE CONTROL for the case above. A Russian catalogue produced by copying the English one
    /// passes "every key exists in both" perfectly, and Studio would come up English with a Russian
    /// language selected. So most values must actually differ.
    ///
    /// Not all of them: "B-Tree", "LSM", "OK" and the language's own name are the same in both by
    /// design, which is why this is a proportion rather than "every value differs".
    /// </summary>
    [Test]
    public void TheRussianCatalogueIsATranslationAndNotACopyTest()
    {
        var english = m_localization.Texts("en");
        var russian = m_localization.Texts("ru");

        var shared = english.Keys.Intersect(russian.Keys).ToList();
        var identical = shared.Count(key => english[key] == russian[key]);

        Assert.That(identical, Is.LessThan(shared.Count / 4),
            $"{identical} of {shared.Count} Russian strings are byte-identical to the English ones - "
            + "that is what an untranslated copy looks like");
    }

    /// <summary>
    /// WS-64 as a check rather than as a rule in a document: a term a person will search for reads the
    /// same in both languages.
    /// </summary>
    [Test]
    public void TermsOfTheEngineAreNotTranslatedTest()
    {
        var english = m_localization.Texts("en");
        var russian = m_localization.Texts("ru");

        var lost = new List<string>();
        var checkedTerms = 0;

        foreach (var (key, value) in english)
        {
            if (!russian.TryGetValue(key, out var translated))
                continue;

            foreach (var term in NOT_TRANSLATED)
            {
                if (!value.Contains(term, StringComparison.Ordinal))
                    continue;

                checkedTerms++;

                if (!translated.Contains(term, StringComparison.Ordinal))
                    lost.Add($"{key}: '{term}' is in the English string and not in the Russian one");
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: if no English string mentions any of these, the loop above asserts nothing.
            Assert.That(checkedTerms, Is.GreaterThan(4),
                "CONTROL: too few strings carry an engine term - this case is not measuring anything");

            Assert.That(lost, Is.Empty, string.Join(Environment.NewLine, lost));
        });
    }

    /// <summary>
    /// A translated string whose placeholders do not match the call site renders the template instead of
    /// the value - which is a defect that only shows on one language, in one window, at run time.
    /// </summary>
    [Test]
    public void PlaceholdersMatchBetweenLanguagesTest()
    {
        var english = m_localization.Texts("en");
        var russian = m_localization.Texts("ru");

        var mismatched = (from key in english.Keys.Intersect(russian.Keys)
                          let inEnglish = CountPlaceholders(english[key])
                          let inRussian = CountPlaceholders(russian[key])
                          where inEnglish != inRussian
                          select $"{key}: {inEnglish} in English, {inRussian} in Russian").ToList();

        Assert.That(mismatched, Is.Empty, string.Join(Environment.NewLine, mismatched));
    }

    private static int CountPlaceholders(string value)
    {
        var count = 0;

        for (var index = 0; index + 2 < value.Length; index++)
        {
            if (value[index] == '{' && char.IsDigit(value[index + 1]) && value[index + 2] == '}')
                count++;
        }

        return count;
    }

    #endregion

    #region Switching

    [Test]
    public void SwitchingTheLanguageChangesTheTextAndSaysSoTest()
    {
        var raised = 0;
        m_localization.LanguageChanged += (_, _) => raised++;

        var english = m_localization["Common.Cancel"];

        m_localization.SetLanguage("ru");

        Assert.Multiple(() =>
        {
            Assert.That(m_localization.Language, Is.EqualTo("ru"));
            Assert.That(m_localization["Common.Cancel"], Is.Not.EqualTo(english));
            Assert.That(m_localization["Common.Cancel"], Is.EqualTo("Отмена"));
            Assert.That(raised, Is.EqualTo(1), "anything holding a rendered string has to be told");
        });
    }

    [Test]
    public void SettingTheSameLanguageSaysNothingTest()
    {
        var raised = 0;
        m_localization.LanguageChanged += (_, _) => raised++;

        m_localization.SetLanguage("en");

        Assert.That(raised, Is.Zero);
    }

    /// <summary>
    /// The language comes out of a settings file a person can edit by hand. A typo there must not stop
    /// Studio from starting.
    /// </summary>
    [Test]
    public void AnUnknownLanguageFallsBackToEnglishTest()
    {
        var service = new LocalizationService("kl");

        Assert.Multiple(() =>
        {
            Assert.That(service.Language, Is.EqualTo("en"));
            Assert.That(service["Common.Cancel"], Is.EqualTo("Cancel"));
        });
    }

    /// <summary>
    /// A missing key renders as itself: visible on screen and greppable, rather than a blank button
    /// nobody notices until a user reports an empty dialog.
    /// </summary>
    [Test]
    public void AMissingKeyRendersAsItselfTest()
    {
        Assert.That(m_localization["No.Such.Key"], Is.EqualTo("No.Such.Key"));
    }

    #endregion

    #region Plurals and formats

    [TestCase(1, "1 row")]
    [TestCase(2, "2 rows")]
    [TestCase(5, "5 rows")]
    [TestCase(0, "0 rows")]
    public void EnglishPluralsTest(long count, string expected)
    {
        Assert.That(m_localization.Plural("Count.Rows", count), Is.EqualTo(expected));
    }

    /// <summary>
    /// The three Russian forms, including the cases that look like exceptions and are the rule: 11 is
    /// "строк" though it ends in 1, 21 is "строка", 0 is "строк".
    /// </summary>
    [TestCase(1, "1 строка")]
    [TestCase(2, "2 строки")]
    [TestCase(4, "4 строки")]
    [TestCase(5, "5 строк")]
    [TestCase(11, "11 строк")]
    [TestCase(12, "12 строк")]
    [TestCase(14, "14 строк")]
    [TestCase(21, "21 строка")]
    [TestCase(22, "22 строки")]
    [TestCase(25, "25 строк")]
    [TestCase(100, "100 строк")]
    [TestCase(101, "101 строка")]
    [TestCase(111, "111 строк")]
    [TestCase(0, "0 строк")]
    public void RussianPluralsTest(long count, string expected)
    {
        m_localization.SetLanguage("ru");

        Assert.That(m_localization.Plural("Count.Rows", count), Is.EqualTo(expected));
    }

    /// <summary>
    /// CONTROL for the case above: a Russian catalogue whose three forms are the same string would pass
    /// every one of those fourteen cases that happens to be checked against that string.
    /// </summary>
    [Test]
    public void TheThreeRussianFormsAreThreeDifferentStringsTest()
    {
        foreach (var (key, forms) in m_localization.Plurals("ru"))
        {
            Assert.That(forms.Values.Distinct().Count(), Is.EqualTo(3),
                $"{key} does not carry three distinct Russian forms");
        }
    }

    /// <summary>
    /// WS-65 at the level of the resource layer: a number inside a sentence is still a number a person
    /// may copy, so it is written invariantly whatever the machine's locale is.
    ///
    /// <para>
    /// <b>The value is a decimal, and that is the whole point of the case.</b> The first version asked
    /// this of an integer count and was powerless: switching the service to
    /// <c>CultureInfo.CurrentCulture</c> under ru-RU left it green, because the default numeric format
    /// for an integer inserts no group separator in any culture. Only a decimal separator tells the two
    /// apart - 4812.50 against 4812,50 - which is exactly the value that will not paste into SQL.
    /// Re-measured with the sabotage in place: red.
    /// </para>
    /// </summary>
    [Test]
    public void ANumberIsWrittenInvariantlyWhateverTheThreadCultureIsTest()
    {
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

            Assert.Multiple(() =>
            {
                Assert.That(m_localization.Format("Status.Size", 4812.50m), Is.EqualTo("4812.50 MB"));
                Assert.That(m_localization.Plural("Count.Rows", 1234567), Is.EqualTo("1234567 rows"));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    /// <summary>
    /// And the other half of the same rule: choosing Russian for the interface does not make a number
    /// Russian. The language of the chrome and the format of a value are separate settings, and this is
    /// the case that would go red if they were ever wired together - which is why it too asks about a
    /// decimal and not only about a count.
    ///
    /// <para>
    /// Measured: formatting through <c>new CultureInfo(Language)</c> instead of the invariant culture
    /// turns this red with <c>4812,50 МБ</c>.
    /// </para>
    /// </summary>
    [Test]
    public void ChoosingRussianDoesNotChangeHowANumberIsWrittenTest()
    {
        m_localization.SetLanguage("ru");

        Assert.Multiple(() =>
        {
            Assert.That(m_localization.Format("Status.Size", 4812.50m), Is.EqualTo("4812.50 МБ"));
            Assert.That(m_localization.Plural("Count.Rows", 1234567), Is.EqualTo("1234567 строк"));
        });
    }

    #endregion
}
