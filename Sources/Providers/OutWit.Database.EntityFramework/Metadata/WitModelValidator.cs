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
    #region Constants

    /// <summary>
    /// The only schema supported by WitDatabase.
    /// </summary>
    private const string SUPPORTED_SCHEMA = "public";

    #endregion

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

    /// <inheritdoc />
    public override void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.Validate(model, logger);

        ValidateSchemas(model);
    }

    /// <summary>
    /// Validates that no custom schemas are used (WitDatabase only supports the default 'public' schema).
    /// </summary>
    private static void ValidateSchemas(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            var schema = entityType.GetSchema();
            if (schema != null && 
                !string.Equals(schema, SUPPORTED_SCHEMA, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"WitDatabase does not support custom schemas. Entity '{entityType.DisplayName()}' " +
                    $"uses schema '{schema}', but only the default schema is supported. " +
                    $"Remove the schema specification or use 'public'.");
            }
        }
    }

    #endregion
}
