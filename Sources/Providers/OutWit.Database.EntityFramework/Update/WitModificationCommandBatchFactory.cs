using Microsoft.EntityFrameworkCore.Update;

namespace OutWit.Database.EntityFramework.Update;

/// <summary>
/// Factory for creating modification command batches for WitDatabase.
/// </summary>
public sealed class WitModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    #region Fields

    private readonly ModificationCommandBatchFactoryDependencies m_dependencies;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitModificationCommandBatchFactory"/> class.
    /// </summary>
    /// <param name="dependencies">The modification command batch factory dependencies.</param>
    public WitModificationCommandBatchFactory(ModificationCommandBatchFactoryDependencies dependencies)
    {
        m_dependencies = dependencies;
    }

    #endregion

    #region Factory Methods

    /// <inheritdoc/>
    public ModificationCommandBatch Create()
    {
        return new SingularModificationCommandBatch(m_dependencies);
    }

    #endregion
}
