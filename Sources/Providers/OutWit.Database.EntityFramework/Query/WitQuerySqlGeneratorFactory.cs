using Microsoft.EntityFrameworkCore.Query;

namespace OutWit.Database.EntityFramework.Query;

/// <summary>
/// Factory for creating <see cref="WitQuerySqlGenerator"/> instances.
/// </summary>
public sealed class WitQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    #region Fields

    private readonly QuerySqlGeneratorDependencies m_dependencies;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitQuerySqlGeneratorFactory"/> class.
    /// </summary>
    /// <param name="dependencies">The query SQL generator dependencies.</param>
    public WitQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies)
    {
        m_dependencies = dependencies;
    }

    #endregion

    #region Factory Methods

    /// <inheritdoc/>
    public QuerySqlGenerator Create()
    {
        return new WitQuerySqlGenerator(m_dependencies);
    }

    #endregion
}
