using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace OutWit.Database.EntityFramework.Update;

/// <summary>
/// Generates SQL for insert, update, and delete operations for WitDatabase.
/// </summary>
public sealed class WitUpdateSqlGenerator : UpdateSqlGenerator
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitUpdateSqlGenerator"/> class.
    /// </summary>
    /// <param name="dependencies">The update SQL generator dependencies.</param>
    public WitUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    #endregion

    #region SQL Generation

    /// <inheritdoc/>
    protected override void AppendValues(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        IReadOnlyList<IColumnModification> operations)
    {
        if (operations.Count == 0)
        {
            commandStringBuilder.Append("DEFAULT VALUES");
            return;
        }

        base.AppendValues(commandStringBuilder, name, schema, operations);
    }

    /// <inheritdoc/>
    public override string GenerateNextSequenceValueOperation(string name, string? schema)
    {
        return $"SELECT INCREMENT('{name}')";
    }

    #endregion
}
