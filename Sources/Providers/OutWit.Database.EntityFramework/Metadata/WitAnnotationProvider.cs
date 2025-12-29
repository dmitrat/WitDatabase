using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace OutWit.Database.EntityFramework.Metadata;

/// <summary>
/// Provides WitDatabase-specific annotations for model elements.
/// </summary>
public sealed class WitAnnotationProvider : RelationalAnnotationProvider
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitAnnotationProvider"/> class.
    /// </summary>
    /// <param name="dependencies">The annotation provider dependencies.</param>
    public WitAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
        : base(dependencies)
    {
    }

    #endregion

    // Use all default implementations from base class
    // No custom annotations needed for basic functionality
}
