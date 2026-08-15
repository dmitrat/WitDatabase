using System.Globalization;
using System.Reflection;

namespace OutWit.Database;

/// <summary>
/// What version of this engine is running, read from the assembly rather than written down.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four places answered <c>1.0.0</c> until 2026-08-15</b> - <c>SELECT VERSION()</c>,
/// <c>WitDbConnection.ServerVersion</c>, and both version rows of
/// <c>GetSchema("DataSourceInformation")</c>, which is what tooling and ORMs read. The engine was on
/// 13.1.1. Each was a literal, so each went stale on its own and nothing could notice.
/// </para>
/// <para>
/// The value is the assembly's INFORMATIONAL version, which is what the csproj's
/// <c>&lt;Version&gt;</c> becomes, cut at the <c>+</c> the SDK appends the commit sha after. It is
/// read once: an assembly's version cannot change while it is loaded.
/// </para>
/// </remarks>
public static class WitDatabaseVersion
{
    #region Fields

    private static readonly Lazy<string> s_text = new(ReadText);

    private static readonly Lazy<string> s_normalized = new(() => Normalize(s_text.Value));

    #endregion

    #region Functions

    /// <summary>
    /// Reads the version off this assembly, falling back to the assembly version when there is no
    /// informational one - which happens only in a build that sets neither.
    /// </summary>
    private static string ReadText()
    {
        var assembly = typeof(WitDatabaseVersion).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            // "13.1.1+89ac429…" - the sha is noise in an answer a person or a tool reads.
            var plus = informational.IndexOf('+');

            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// The same version in the shape ADO.NET's <c>DataSourceProductVersionNormalized</c> is compared
    /// in: two digits of major, two of minor, four of build, so that a string comparison orders
    /// versions correctly.
    /// </summary>
    /// <remarks>
    /// A pre-release suffix is dropped rather than encoded - <c>13.1.1-rc.1</c> normalises to
    /// <c>13.01.0001</c>. The field exists to be COMPARED, and there is no ordering of suffixes that
    /// a consumer could rely on.
    /// </remarks>
    private static string Normalize(string version)
    {
        var numeric = version.Split('-', '+')[0];
        var parts = numeric.Split('.');

        var major = Part(parts, 0);
        var minor = Part(parts, 1);
        var build = Part(parts, 2);

        return string.Format(CultureInfo.InvariantCulture, "{0:00}.{1:00}.{2:0000}", major, minor, build);
    }

    private static int Part(string[] parts, int index) =>
        parts.Length > index && int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    #endregion

    #region Properties

    /// <summary>The engine's version as a person would write it: <c>13.1.1</c>.</summary>
    public static string Text => s_text.Value;

    /// <summary>The same, zero-padded for comparison: <c>13.01.0001</c>.</summary>
    public static string Normalized => s_normalized.Value;

    #endregion
}
