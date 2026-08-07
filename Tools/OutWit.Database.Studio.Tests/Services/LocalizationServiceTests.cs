using System.Globalization;
using System.Reflection;
using OutWit.Database.Studio.Services.Localization;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The interface language (WS-63, WS-64, WS-65).
///
/// <para>
/// <b>Every case here walks the languages that shipped rather than naming two.</b> The first version
/// compared "en" against "ru" by name, which meant a third catalogue would have been added, embedded,
/// shown in the picker - and checked by nothing. A guard that only knows about the languages that
/// existed when it was written is a guard that stops guarding the moment the thing it guards grows.
/// </para>
/// <para>
/// What is worth checking is not "does a lookup return a string" - it is the ways a localisation
/// quietly stops being one: a language that is a copy of the base, a term that was translated when it
/// should not have been, a plural with the wrong number of forms, and a number that started following
/// the thread culture.
/// </para>
/// </summary>
[TestFixture]
public class LocalizationServiceTests
{
    #region Constants

    /// <summary>
    /// Terms that must read the same in every language (WS-64). A person searches the documentation,
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

    private IEnumerable<string> Languages => m_localization.Available.Select(language => language.Code);

    private IEnumerable<string> Translations =>
        Languages.Where(code => code != LocalizationService.BASE_LANGUAGE);

    #endregion

    #region A language is a file

    /// <summary>
    /// <b>The list of languages is discovered, not written down.</b> This is the whole of "adding a
    /// language is adding a file": if the picker were a literal in the constructor, an embedded
    /// catalogue could exist and never be offered, and this case is what would notice.
    /// </summary>
    [Test]
    public void TheLanguagesAreTheCataloguesThatShippedTest()
    {
        var embedded = Assembly.GetAssembly(typeof(LocalizationService))!
            .GetManifestResourceNames()
            .Where(name => name.Contains(".Resources.Strings.", StringComparison.Ordinal))
            .Select(name => name.Split('.')[^2])
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(embedded, Is.Not.Empty, "CONTROL: no catalogue is embedded at all");

            Assert.That(Languages.OrderBy(code => code, StringComparer.Ordinal), Is.EqualTo(embedded),
                "the offered languages are exactly the catalogues in the assembly");
        });
    }

    /// <summary>
    /// The base language has to be there: everything is compared against it and an unknown code falls
    /// back to it.
    /// </summary>
    [Test]
    public void TheBaseLanguageIsAlwaysPresentAndFirstTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Languages, Does.Contain(LocalizationService.BASE_LANGUAGE));
            Assert.That(m_localization.Available[0].Code, Is.EqualTo(LocalizationService.BASE_LANGUAGE),
                "so a picker does not reorder itself when a language is added");
        });
    }

    /// <summary>
    /// Each catalogue names itself in its own language. "Russian" is no help to somebody who cannot
    /// read the interface they are trying to change.
    /// </summary>
    [Test]
    public void EveryLanguageNamesItselfInItsOwnLanguageTest()
    {
        foreach (var language in m_localization.Available)
        {
            Assert.That(language.NativeName, Is.Not.Empty.And.Not.EqualTo(language.Code),
                $"{language.Code} has no $language header, so the picker would show its code");
        }
    }

    /// <summary>
    /// A catalogue declares a plural FAMILY rather than getting a branch in the code, so a new language
    /// that behaves like one that is already here needs no code at all. One this build does not know
    /// would silently fall back to one-and-other, which is what this refuses.
    /// </summary>
    [Test]
    public void EveryCatalogueDeclaresAFamilyThisBuildKnowsTest()
    {
        foreach (var language in Languages)
        {
            Assert.That(PluralRules.IsKnown(m_localization.PluralFamilyOf(language)), Is.True,
                $"{language} declares a plural family this build does not implement");
        }
    }

    #endregion

    #region The catalogues

    /// <summary>
    /// CONTROL, and it is first because every other case here is relational: "the two agree" proves
    /// nothing about two empty catalogues, and neither does "no term was translated".
    /// </summary>
    [Test]
    public void EveryCatalogueCarriesStringsTest()
    {
        foreach (var language in Languages)
        {
            Assert.That(m_localization.Texts(language), Has.Count.GreaterThan(30),
                $"CONTROL: {language} is missing or nearly empty, so every comparison below is vacuous");
        }
    }

    [Test]
    public void EveryLanguageHasEveryKeyOfTheBaseTest()
    {
        var expected = m_localization.Texts(LocalizationService.BASE_LANGUAGE).Keys.ToHashSet();

        foreach (var language in Translations)
        {
            var actual = m_localization.Texts(language).Keys.ToHashSet();

            Assert.Multiple(() =>
            {
                Assert.That(expected.Except(actual), Is.Empty, $"keys with no {language} translation");
                Assert.That(actual.Except(expected), Is.Empty, $"keys {language} has and the base does not");
            });
        }
    }

    [Test]
    public void EveryLanguageHasEveryPluralOfTheBaseTest()
    {
        var expected = m_localization.Plurals(LocalizationService.BASE_LANGUAGE).Keys.ToHashSet();

        foreach (var language in Translations)
        {
            var actual = m_localization.Plurals(language).Keys.ToHashSet();

            Assert.Multiple(() =>
            {
                Assert.That(expected.Except(actual), Is.Empty, $"plurals with no {language} forms");
                Assert.That(actual.Except(expected), Is.Empty, $"plurals {language} has and the base does not");
            });
        }
    }

    /// <summary>
    /// THE NEGATIVE CONTROL for the case above. A catalogue produced by copying the base one passes
    /// "every key exists in both" perfectly, and Studio would come up English with another language
    /// selected. So most values must actually differ.
    ///
    /// Not all of them: "B-Tree", "LSM" and "OK" are the same in many languages by design, which is
    /// why this is a proportion rather than "every value differs".
    /// </summary>
    [Test]
    public void ATranslationIsNotACopyOfTheBaseTest()
    {
        var expected = m_localization.Texts(LocalizationService.BASE_LANGUAGE);

        foreach (var language in Translations)
        {
            var actual = m_localization.Texts(language);
            var shared = expected.Keys.Intersect(actual.Keys).ToList();
            var identical = shared.Count(key => expected[key] == actual[key]);

            Assert.That(identical, Is.LessThan(shared.Count / 4),
                $"{identical} of {shared.Count} {language} strings are byte-identical to the base - "
                + "that is what an untranslated copy looks like");
        }
    }

    /// <summary>
    /// WS-64 as a check rather than as a rule in a document: a term a person will search for reads the
    /// same in every language.
    /// </summary>
    [Test]
    public void TermsOfTheEngineAreNotTranslatedTest()
    {
        var expected = m_localization.Texts(LocalizationService.BASE_LANGUAGE);

        var lost = new List<string>();
        var checkedTerms = 0;

        foreach (var language in Translations)
        {
            var actual = m_localization.Texts(language);

            foreach (var (key, value) in expected)
            {
                if (!actual.TryGetValue(key, out var translated))
                    continue;

                foreach (var term in NOT_TRANSLATED)
                {
                    if (!value.Contains(term, StringComparison.Ordinal))
                        continue;

                    checkedTerms++;

                    if (!translated.Contains(term, StringComparison.Ordinal))
                        lost.Add($"{language} {key}: '{term}' is in the base string and not in this one");
                }
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: if no base string mentions any of these, the loop above asserts nothing.
            Assert.That(checkedTerms, Is.GreaterThan(4),
                "CONTROL: too few strings carry an engine term - this case is not measuring anything");

            Assert.That(lost, Is.Empty, string.Join(Environment.NewLine, lost));
        });
    }

    /// <summary>
    /// A translated string whose placeholders do not match the call site renders the template instead
    /// of the value - a defect that only shows in one language, in one window, at run time.
    /// </summary>
    [Test]
    public void PlaceholdersMatchTheBaseTest()
    {
        var expected = m_localization.Texts(LocalizationService.BASE_LANGUAGE);
        var mismatched = new List<string>();

        foreach (var language in Translations)
        {
            var actual = m_localization.Texts(language);

            mismatched.AddRange(from key in expected.Keys.Intersect(actual.Keys)
                                let inBase = CountPlaceholders(expected[key])
                                let inThis = CountPlaceholders(actual[key])
                                where inBase != inThis
                                select $"{language} {key}: {inBase} in the base, {inThis} here");
        }

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

        var before = m_localization["Common.Cancel"];

        m_localization.SetLanguage("ru");

        Assert.Multiple(() =>
        {
            Assert.That(m_localization.Language, Is.EqualTo("ru"));
            Assert.That(m_localization["Common.Cancel"], Is.Not.EqualTo(before));
            Assert.That(m_localization["Common.Cancel"], Is.EqualTo("Отмена"));
            Assert.That(raised, Is.EqualTo(1), "anything holding a rendered string has to be told");
        });
    }

    [Test]
    public void SettingTheSameLanguageSaysNothingTest()
    {
        var raised = 0;
        m_localization.LanguageChanged += (_, _) => raised++;

        m_localization.SetLanguage(LocalizationService.BASE_LANGUAGE);

        Assert.That(raised, Is.Zero);
    }

    /// <summary>
    /// The language comes out of a settings file a person can edit by hand. A typo there must not stop
    /// Studio from starting.
    /// </summary>
    [Test]
    public void AnUnknownLanguageFallsBackToTheBaseTest()
    {
        var service = new LocalizationService("kl");

        Assert.Multiple(() =>
        {
            Assert.That(service.Language, Is.EqualTo(LocalizationService.BASE_LANGUAGE));
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

    #region Plurals

    /// <summary>
    /// Every plural entry carries exactly the forms its language's family uses, and they are different
    /// strings. This is what makes the family declaration mean something: a Slavic catalogue with two
    /// forms would otherwise fall back quietly and be wrong for 5, 11 and 25.
    /// </summary>
    [Test]
    public void EveryPluralCarriesTheFormsItsFamilyUsesTest()
    {
        var checkedEntries = 0;

        foreach (var language in Languages)
        {
            var forms = PluralRules.FormsOf(m_localization.PluralFamilyOf(language));

            foreach (var (key, entry) in m_localization.Plurals(language))
            {
                checkedEntries++;

                Assert.That(entry.Keys.OrderBy(form => form), Is.EqualTo(forms.OrderBy(form => form)),
                    $"{language} {key} does not carry the forms of its declared family");

                Assert.That(entry.Values.Distinct().Count(), Is.EqualTo(forms.Count),
                    $"{language} {key} repeats a form, so at least one count reads wrongly");
            }
        }

        Assert.That(checkedEntries, Is.GreaterThan(4),
            "CONTROL: too few plural entries - this case is not measuring anything");
    }

    [TestCase(1, "1 row")]
    [TestCase(2, "2 rows")]
    [TestCase(5, "5 rows")]
    [TestCase(0, "0 rows")]
    public void TheOneOtherFamilyTest(long count, string expected)
    {
        Assert.That(m_localization.Plural("Count.Rows", count), Is.EqualTo(expected));
    }

    /// <summary>
    /// The Slavic family through a real catalogue, including the cases that look like exceptions and
    /// are the rule: 11 is "строк" though it ends in 1, 21 is "строка", 0 is "строк".
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
    public void TheSlavicFamilyTest(long count, string expected)
    {
        m_localization.SetLanguage("ru");

        Assert.That(m_localization.Plural("Count.Rows", count), Is.EqualTo(expected));
    }

    /// <summary>
    /// And a family with no plural at all - Chinese, Japanese, Turkish - is answered without code being
    /// written for the language, which is the point of families. No catalogue uses it yet; the rule
    /// exists so that one can be added as a file.
    /// </summary>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(5)]
    [TestCase(21)]
    public void TheOneFormFamilyNeverChangesTheWordTest(long count)
    {
        Assert.That(PluralRules.FormFor(PluralRules.FAMILY_ONE_FORM, count), Is.EqualTo(PluralRules.OTHER));
    }

    #endregion

    #region The formats stay out of it (WS-65)

    /// <summary>
    /// A number inside a sentence is still a number a person may copy, so it is written invariantly
    /// whatever the machine's locale is.
    ///
    /// <para>
    /// <b>The value is a decimal, and that is the whole point of the case.</b> The first version asked
    /// this of an integer count and was powerless: switching the service to
    /// <c>CultureInfo.CurrentCulture</c> under ru-RU left it green, because the default numeric format
    /// for an integer inserts no group separator in any culture. Only a decimal separator tells the two
    /// apart - 4812.50 against 4812,50 - which is exactly the value that will not paste into SQL.
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
    /// And the other half of the same rule: choosing a language does not make a number follow it. The
    /// language of the chrome and the format of a value are separate settings.
    ///
    /// <para>
    /// Measured: formatting through <c>new CultureInfo(Language)</c> instead of the invariant culture
    /// turns this red with <c>4812,50 МБ</c>.
    /// </para>
    /// </summary>
    [Test]
    public void ChoosingALanguageDoesNotChangeHowANumberIsWrittenTest()
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
