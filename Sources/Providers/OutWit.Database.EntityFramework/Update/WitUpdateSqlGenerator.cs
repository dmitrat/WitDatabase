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

    /// <inheritdoc/>
    protected override void AppendWhereCondition(
        StringBuilder commandStringBuilder,
        IColumnModification columnModification,
        bool useOriginalValue)
    {
        var columnName = Dependencies.SqlGenerationHelper.DelimitIdentifier(columnModification.ColumnName);

        if (useOriginalValue && columnModification.OriginalValue == null)
        {
            commandStringBuilder.Append(columnName).Append(" IS NULL");
        }
        else if (!useOriginalValue && columnModification.Value == null)
        {
            commandStringBuilder.Append(columnName).Append(" IS NULL");
        }
        else
        {
            commandStringBuilder.Append(columnName).Append(" = ");
            if (useOriginalValue)
            {
                commandStringBuilder.Append(
                    Dependencies.SqlGenerationHelper.GenerateParameterNamePlaceholder(
                        columnModification.OriginalParameterName!));
            }
            else
            {
                commandStringBuilder.Append(
                    Dependencies.SqlGenerationHelper.GenerateParameterNamePlaceholder(
                        columnModification.ParameterName!));
            }
        }
    }

    #endregion
}
