using Microsoft.EntityFrameworkCore.Storage;

namespace OutWit.Database.EntityFramework.Storage;

/// <summary>
/// Provides SQL generation helper methods for WitDatabase.
/// </summary>
public sealed class WitSqlGenerationHelper : RelationalSqlGenerationHelper
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitSqlGenerationHelper"/> class.
    /// </summary>
    /// <param name="dependencies">The SQL generation helper dependencies.</param>
    public WitSqlGenerationHelper(RelationalSqlGenerationHelperDependencies dependencies)
        : base(dependencies)
    {
    }

    #endregion

    #region Identifier Handling

    /// <inheritdoc/>
    public override string DelimitIdentifier(string identifier)
    {
        return $"\"{EscapeIdentifier(identifier)}\"";
    }

    /// <inheritdoc/>
    public override void DelimitIdentifier(System.Text.StringBuilder builder, string identifier)
    {
        builder.Append('"');
        EscapeIdentifier(builder, identifier);
        builder.Append('"');
    }

    /// <inheritdoc/>
    public override string EscapeIdentifier(string identifier)
    {
        return identifier.Replace("\"", "\"\"");
    }

    /// <inheritdoc/>
    public override void EscapeIdentifier(System.Text.StringBuilder builder, string identifier)
    {
        var initialLength = builder.Length;
        builder.Append(identifier);

        for (var i = builder.Length - 1; i >= initialLength; i--)
        {
            if (builder[i] == '"')
            {
                builder.Insert(i, '"');
            }
        }
    }

    #endregion

    #region Parameter Handling

    /// <inheritdoc/>
    public override string GenerateParameterName(string name)
    {
        return name.StartsWith('@') ? name : $"@{name}";
    }

    /// <inheritdoc/>
    public override void GenerateParameterName(System.Text.StringBuilder builder, string name)
    {
        if (!name.StartsWith('@'))
        {
            builder.Append('@');
        }
        builder.Append(name);
    }

    /// <inheritdoc/>
    public override string GenerateParameterNamePlaceholder(string name)
    {
        return GenerateParameterName(name);
    }

    /// <inheritdoc/>
    public override void GenerateParameterNamePlaceholder(System.Text.StringBuilder builder, string name)
    {
        GenerateParameterName(builder, name);
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override string StatementTerminator => ";";

    /// <inheritdoc/>
    public override string BatchTerminator => string.Empty;

    #endregion
}
