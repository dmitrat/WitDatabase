using System.Globalization;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// A size, the way a person reads one.
/// </summary>
/// <remarks>
/// <para>
/// One implementation, because there were two the moment the «База» tab needed sizes as well as the
/// Open dialog, and two formatters of the same thing drift.
/// </para>
/// <para>
/// <b>The units are deliberately not translated.</b> <c>KB</c> and <c>MB</c> are the same in every
/// interface a developer has ever used, and a translated unit is one nobody can match against what the
/// file manager says. The NUMBER is invariant for the same reason a value the interface shows must be
/// pasteable into SQL unchanged.
/// </para>
/// </remarks>
public static class ByteSize
{
    private static readonly string[] UNITS = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < UNITS.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return size.ToString(unit == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + UNITS[unit];
    }
}
