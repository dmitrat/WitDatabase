using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace OutWit.Database.EntityFramework.Metadata;

/// <summary>
/// Validates an <see cref="IModel"/> for WitDatabase compatibility.
/// </summary>
public sealed class WitModelValidator : RelationalModelValidator
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitModelValidator"/> class.
    /// </summary>
    /// <param name="dependencies">The model validator dependencies.</param>
    /// <param name="relationalDependencies">The relational model validator dependencies.</param>
    public WitModelValidator(
        ModelValidatorDependencies dependencies,
        RelationalModelValidatorDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    #endregion

    // Use all default validation from base class
    // WitDatabase supports all standard relational features
}
