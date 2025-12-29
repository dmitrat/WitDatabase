using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace OutWit.Database.EntityFramework.Metadata;

/// <summary>
/// Provides WitDatabase-specific annotations for model elements.
/// </summary>
public sealed class WitAnnotationProvider : RelationalAnnotationProvider
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitAnnotationProvider"/> class.
    /// </summary>
    /// <param name="dependencies">The annotation provider dependencies.</param>
    public WitAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
        : base(dependencies)
    {
    }

    #endregion

    #region Annotation Providers

    /// <inheritdoc/>
    public override IEnumerable<IAnnotation> For(IRelationalModel model, bool designTime)
    {
        return base.For(model, designTime);
    }

    /// <inheritdoc/>
    public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
    {
        return base.For(table, designTime);
    }

    /// <inheritdoc/>
    public override IEnumerable<IAnnotation> For(IColumn column, bool designTime)
    {
        // Check for autoincrement
        var property = column.PropertyMappings.FirstOrDefault()?.Property;
        if (property != null && 
            property.ValueGenerated == ValueGenerated.OnAdd &&
            IsIntegerType(property.ClrType))
        {
            yield return new Annotation("WitDb:Autoincrement", true);
        }

        foreach (var annotation in base.For(column, designTime))
        {
            yield return annotation;
        }
    }

    /// <inheritdoc/>
    public override IEnumerable<IAnnotation> For(ITableIndex index, bool designTime)
    {
        return base.For(index, designTime);
    }

    /// <inheritdoc/>
    public override IEnumerable<IAnnotation> For(IForeignKeyConstraint foreignKey, bool designTime)
    {
        return base.For(foreignKey, designTime);
    }

    #endregion

    #region Helpers

    private static bool IsIntegerType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType == typeof(int) ||
               underlyingType == typeof(long) ||
               underlyingType == typeof(short) ||
               underlyingType == typeof(byte) ||
               underlyingType == typeof(uint) ||
               underlyingType == typeof(ulong) ||
               underlyingType == typeof(ushort) ||
               underlyingType == typeof(sbyte);
    }

    #endregion
}
