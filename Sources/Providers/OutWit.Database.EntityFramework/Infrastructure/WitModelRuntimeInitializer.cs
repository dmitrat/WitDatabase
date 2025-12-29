using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace OutWit.Database.EntityFramework.Infrastructure;

/// <summary>
/// Custom relational model runtime initializer for WitDatabase.
/// </summary>
/// <remarks>
/// This initializer ensures that the RelationalModel is created with the correct RuntimeModel reference.
/// The base class creates a factory that captures the design-time model, but EF Core expects
/// the RelationalModel to reference the RuntimeModel for proper table mappings during SaveChanges.
/// </remarks>
public sealed class WitModelRuntimeInitializer : RelationalModelRuntimeInitializer
{
    private readonly IRelationalTypeMappingSource m_typeMappingSource;
    private readonly IRelationalAnnotationProvider m_annotationProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="WitModelRuntimeInitializer"/> class.
    /// </summary>
    /// <param name="dependencies">The model runtime initializer dependencies.</param>
    /// <param name="relationalDependencies">The relational model runtime initializer dependencies.</param>
    /// <param name="typeMappingSource">The relational type mapping source.</param>
    /// <param name="annotationProvider">The relational annotation provider.</param>
    public WitModelRuntimeInitializer(
        ModelRuntimeInitializerDependencies dependencies,
        RelationalModelRuntimeInitializerDependencies relationalDependencies,
        IRelationalTypeMappingSource typeMappingSource,
        IRelationalAnnotationProvider annotationProvider)
        : base(dependencies, relationalDependencies)
    {
        m_typeMappingSource = typeMappingSource;
        m_annotationProvider = annotationProvider;
    }

    /// <inheritdoc />
    protected override void InitializeModel(IModel model, bool designTime, bool prevalidation)
    {
        if (prevalidation)
        {
            // Call base for prevalidation phase
            base.InitializeModel(model, designTime, prevalidation);
            return;
        }

        // Check if factory already exists (could be set by base class or previous call)
        var existingFactory = ((IAnnotatable)model).FindRuntimeAnnotation(RelationalAnnotationNames.RelationalModelFactory);
        if (existingFactory != null)
        {
            // Factory already exists, just call base
            base.InitializeModel(model, designTime, prevalidation);
            return;
        }

        // Add relational model dependencies that base class would add
        var modelDependencies = ((IAnnotatable)model).FindRuntimeAnnotation(RelationalAnnotationNames.ModelDependencies);
        if (modelDependencies == null)
        {
            ((IAnnotatable)model).AddRuntimeAnnotation(
                RelationalAnnotationNames.ModelDependencies,
                RelationalDependencies);
        }

        // Create a factory that lazily resolves the RuntimeModel from ReadOnlyModel annotation.
        // When InitializeModel is called, the model is the design-time model (Internal.Model).
        // The ReadOnlyModel annotation is set later when RuntimeModel is created.
        // By capturing the design-time model and looking up ReadOnlyModel at factory invocation time,
        // we ensure the RelationalModel is created with the correct RuntimeModel reference.
        var designTimeModel = model;
        var typeMappingSource = m_typeMappingSource;
        var annotationProvider = m_annotationProvider;

        ((IAnnotatable)model).AddRuntimeAnnotation(
            RelationalAnnotationNames.RelationalModelFactory,
            () =>
            {
                // At invocation time, ReadOnlyModel should point to the RuntimeModel
                var runtimeModel = ((IAnnotatable)designTimeModel).FindRuntimeAnnotation("ReadOnlyModel")?.Value as IModel;
                var modelToUse = runtimeModel ?? designTimeModel;

                return RelationalModel.Create(modelToUse, annotationProvider, typeMappingSource, designTime: false);
            });
    }
}
