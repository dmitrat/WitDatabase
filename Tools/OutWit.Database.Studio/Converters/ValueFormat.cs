using System.Globalization;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// How a value is written on screen (WS-65).
///
/// <para>
/// <b>Not the interface language, and not the machine's locale.</b> In an ordinary application a
/// number follows the locale; in a database tool the number is copied out of the grid and pasted into
/// a statement, and a locale that writes <c>4 812,50</c> has just produced something the parser will
/// refuse. So the default is invariant and ISO whatever the machine is set to, and following the
/// system is an explicit choice that says what it costs.
/// </para>
/// <para>
/// <see cref="Current"/> is set from the settings when they change. Everything that formats takes the
/// format as an argument; only the Avalonia converter reads the static, because a converter has
/// nowhere to be handed one from.
/// </para>
/// </summary>
public sealed record ValueFormat(string DateTime, string Number, string Binary)
{
    #region Constants

    public const string ISO = "Iso";
    public const string INVARIANT = "Invariant";
    public const string SYSTEM = "System";

    public const string BINARY_SIZE = "Size";
    public const string BINARY_HEX = "Hex";
    public const string BINARY_BASE64 = "Base64";

    #endregion

    #region Properties

    /// <summary>What a fresh installation uses, and what every test that does not say otherwise uses.</summary>
    public static ValueFormat Default { get; } = new(ISO, INVARIANT, BINARY_SIZE);

    /// <summary>
    /// The format the application is showing. Assigned when the setting changes; nothing reads it
    /// except the converter that has no other way to be told.
    /// </summary>
    public static ValueFormat Current { get; set; } = Default;

    /// <summary>The culture numbers are written with.</summary>
    public CultureInfo NumberCulture =>
        Number == SYSTEM ? CultureInfo.CurrentCulture : CultureInfo.InvariantCulture;

    /// <summary>The culture dates are written with when the format is not ISO.</summary>
    public CultureInfo DateCulture =>
        DateTime == SYSTEM ? CultureInfo.CurrentCulture : CultureInfo.InvariantCulture;

    /// <summary>Whether dates are written in ISO 8601 rather than in the system's own order.</summary>
    public bool DatesAreIso => DateTime != SYSTEM;

    #endregion
}
