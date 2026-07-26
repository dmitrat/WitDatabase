using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace OutWit.Database.EntityFramework.Metadata;

/// <summary>
/// Builds the model-building convention set for WitDatabase.
/// </summary>
/// <remarks>
/// Every relational EF Core provider must register an <see cref="IProviderConventionSetBuilder"/>
/// derived from <see cref="RelationalConventionSetBuilder"/> (SQL Server, SQLite and Npgsql all
/// ship one). Without it EF Core falls back to the *core* <see cref="ProviderConventionSetBuilder"/>
/// and the entire relational convention set is absent — most visibly
/// <c>TableNameFromDbSetConvention</c>, so default table names come from the entity CLR type
/// instead of the <c>DbSet</c> property and diverge from every other provider for the same model,
/// but also <c>RelationalValueGenerationConvention</c> (identity and computed columns),
/// <c>RelationalDbFunctionConvention</c> (<c>HasDbFunction</c>), <c>SharedTableConvention</c> and
/// <c>StoreGenerationConvention</c>.
/// </remarks>
public class WitConventionSetBuilder : RelationalConventionSetBuilder
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitConventionSetBuilder"/> class.
    /// </summary>
    /// <param name="dependencies">The provider convention set builder dependencies.</param>
    /// <param name="relationalDependencies">The relational convention set builder dependencies.</param>
    public WitConventionSetBuilder(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    #endregion

    // The base relational convention set is correct for WitDatabase; no provider-specific
    // conventions are needed yet. Override CreateConventionSet() when one is.
}
