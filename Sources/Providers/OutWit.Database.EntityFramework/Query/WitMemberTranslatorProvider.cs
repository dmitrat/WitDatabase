using Microsoft.EntityFrameworkCore.Query;

namespace OutWit.Database.EntityFramework.Query;

/// <summary>
/// Provider for WitDatabase member translators.
/// </summary>
public sealed class WitMemberTranslatorProvider : RelationalMemberTranslatorProvider
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitMemberTranslatorProvider"/> class.
    /// </summary>
    /// <param name="dependencies">The dependencies.</param>
    public WitMemberTranslatorProvider(RelationalMemberTranslatorProviderDependencies dependencies)
        : base(dependencies)
    {
        var sqlExpressionFactory = dependencies.SqlExpressionFactory;

        AddTranslators(
        [
            new WitMemberTranslator(sqlExpressionFactory)
        ]);
    }

    #endregion
}
