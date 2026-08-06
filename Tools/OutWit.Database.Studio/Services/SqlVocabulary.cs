using System.Reflection;
using System.Xml.Linq;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// The words of the language, read from the file that colours them.
///
/// <c>WitSql.xshd</c> already lists every keyword, type name and function the editor highlights, and
/// completion has to offer the same set (WS-24). Reading the one file is what keeps them from
/// disagreeing: a keyword added to the grammar and to the highlighting but not to a second hand-kept
/// list would be coloured and never suggested, which reads to a user as the editor knowing a word the
/// completion does not.
/// </summary>
public static class SqlVocabulary
{
    #region Constants

    private const string RESOURCE = "OutWit.Database.Studio.Syntax.WitSql.xshd";

    #endregion

    #region Fields

    private static readonly Lock LOCK = new();

    private static IReadOnlyList<string>? m_keywords;
    private static IReadOnlyList<string>? m_functions;
    private static IReadOnlyList<string>? m_types;

    #endregion

    #region Properties

    public static IReadOnlyList<string> Keywords
    {
        get { Load(); return m_keywords!; }
    }

    public static IReadOnlyList<string> Functions
    {
        get { Load(); return m_functions!; }
    }

    public static IReadOnlyList<string> DataTypes
    {
        get { Load(); return m_types!; }
    }

    #endregion

    #region Functions

    private static void Load()
    {
        if (m_keywords != null)
            return;

        lock (LOCK)
        {
            if (m_keywords != null)
                return;

            var keywords = new List<string>();
            var functions = new List<string>();
            var types = new List<string>();

            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(RESOURCE);

                if (stream != null)
                {
                    var document = XDocument.Load(stream);

                    foreach (var group in document.Descendants().Where(e => e.Name.LocalName == "Keywords"))
                    {
                        var colour = group.Attribute("color")?.Value ?? string.Empty;

                        var words = group.Elements()
                            .Where(e => e.Name.LocalName == "Word")
                            .Select(e => e.Value.Trim())
                            .Where(word => word.Length > 1);

                        var target = colour switch
                        {
                            "Function" => functions,
                            "DataType" => types,
                            _ => keywords
                        };

                        target.AddRange(words);
                    }
                }
            }
            catch (Exception)
            {
                // Completion without keywords still offers the schema, which is the half a person
                // cannot type from memory. A missing resource is not a reason to fail an editor.
            }

            m_functions = Sorted(functions);
            m_types = Sorted(types);

            // The highlighting file lists a few words twice under two different colours - REPLACE is
            // both a keyword and a function there. A word is one thing in a completion list, and the
            // more specific answer is the useful one, so the function and type lists win.
            m_keywords = Sorted(keywords)
                .Except(m_functions, StringComparer.Ordinal)
                .Except(m_types, StringComparer.Ordinal)
                .ToList();
        }
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> words)
    {
        return words
            .Select(word => word.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToList();
    }

    #endregion
}
