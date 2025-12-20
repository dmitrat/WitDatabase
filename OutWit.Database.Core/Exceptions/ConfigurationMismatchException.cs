namespace OutWit.Database.Core.Exceptions;

/// <summary>
/// Exception thrown when the database configuration doesn't match what was expected.
/// </summary>
public sealed class ConfigurationMismatchException : Exception
{
    #region Constructors

    public ConfigurationMismatchException(ProviderMetadata expected, ProviderMetadata actual,
        IReadOnlyList<string> mismatches)
        : base(FormatMessage(mismatches))
    {
        ExpectedMetadata = expected;
        ActualMetadata = actual;
        Mismatches = mismatches;
    }

    #endregion

    #region Functions

    private static string FormatMessage(IReadOnlyList<string> mismatches)
    {
        return $"Database configuration mismatch:\n" + string.Join("\n", mismatches.Select(m => $"  - {m}"));
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the expected configuration.
    /// </summary>
    public ProviderMetadata ExpectedMetadata { get; }

    /// <summary>
    /// Gets the actual configuration found in the database.
    /// </summary>
    public ProviderMetadata ActualMetadata { get; }

    /// <summary>
    /// Gets the list of mismatched settings.
    /// </summary>
    public IReadOnlyList<string> Mismatches { get; }

    #endregion
}