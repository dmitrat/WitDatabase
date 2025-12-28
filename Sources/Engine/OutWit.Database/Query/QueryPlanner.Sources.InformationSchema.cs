using OutWit.Database.Definitions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Schema;

namespace OutWit.Database.Query;

/// <summary>
/// INFORMATION_SCHEMA iterator creation for QueryPlanner.
/// </summary>
public sealed partial class QueryPlanner
{
    #region INFORMATION_SCHEMA Iterator

    /// <summary>
    /// Creates an iterator for INFORMATION_SCHEMA virtual tables.
    /// </summary>
    private IResultIterator CreateInformationSchemaIterator(TableSourceSimple simple)
    {
        // Parse the view name from INFORMATION_SCHEMA.VIEW_NAME
        var tableName = simple.TableName;
        var viewName = tableName.Contains('.')
            ? tableName.Split('.', 2)[1]
            : tableName;

        // Get schema catalog from database
        if (m_context.Database is not WitSqlEngine engine)
        {
            throw new InvalidOperationException("INFORMATION_SCHEMA is only available with WitSqlEngine");
        }

        var catalog = engine.Catalog;

        return viewName.ToUpperInvariant() switch
        {
            "TABLES" => new IteratorInformationSchema(
                catalog.GetInformationSchemaTables(),
                SchemaCatalog.GetInformationSchemaTablesColumns(),
                SchemaCatalog.GetInformationSchemaTablesColumnTypes()),
                
            "COLUMNS" => new IteratorInformationSchema(
                catalog.GetInformationSchemaColumns(),
                SchemaCatalog.GetInformationSchemaColumnsColumns(),
                SchemaCatalog.GetInformationSchemaColumnsColumnTypes()),
                
            "KEY_COLUMN_USAGE" => new IteratorInformationSchema(
                catalog.GetInformationSchemaKeyColumnUsage(),
                SchemaCatalog.GetInformationSchemaKeyColumnUsageColumns(),
                SchemaCatalog.GetInformationSchemaKeyColumnUsageColumnTypes()),
                
            "TABLE_CONSTRAINTS" => new IteratorInformationSchema(
                catalog.GetInformationSchemaTableConstraints(),
                SchemaCatalog.GetInformationSchemaTableConstraintsColumns(),
                SchemaCatalog.GetInformationSchemaTableConstraintsColumnTypes()),
                
            "REFERENTIAL_CONSTRAINTS" => new IteratorInformationSchema(
                catalog.GetInformationSchemaReferentialConstraints(),
                SchemaCatalog.GetInformationSchemaReferentialConstraintsColumns(),
                SchemaCatalog.GetInformationSchemaReferentialConstraintsColumnTypes()),
                
            "INDEXES" => new IteratorInformationSchema(
                catalog.GetInformationSchemaIndexes(),
                SchemaCatalog.GetInformationSchemaIndexesColumns(),
                SchemaCatalog.GetInformationSchemaIndexesColumnTypes()),
                
            "VIEWS" => new IteratorInformationSchema(
                catalog.GetInformationSchemaViews(),
                SchemaCatalog.GetInformationSchemaViewsColumns(),
                SchemaCatalog.GetInformationSchemaViewsColumnTypes()),
                
            _ => throw new InvalidOperationException($"Unknown INFORMATION_SCHEMA view: {viewName}")
        };
    }

    #endregion
}
