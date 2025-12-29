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

    #region Validation

    /// <inheritdoc/>
    public override void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.Validate(model, logger);

        ValidateNoSchemas(model, logger);
    }

    private static void ValidateNoSchemas(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        // WitDatabase doesn't support schemas
        foreach (var entityType in model.GetEntityTypes())
        {
            var schema = entityType.GetSchema();
            if (!string.IsNullOrEmpty(schema))
            {
                throw new InvalidOperationException(
                    $"WitDatabase does not support schemas. Entity '{entityType.DisplayName()}' is mapped to schema '{schema}'.");
            }
        }
    }

    #endregion
}
