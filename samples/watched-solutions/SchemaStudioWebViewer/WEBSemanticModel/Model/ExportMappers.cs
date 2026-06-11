using SchemaStudio.Data.Models;

namespace SchemaStudioWebViewer.WEBSemanticModel.Model;

public static class ExportMappers
{
    public static List<SourceTableDto> ToSourceTableDtos(this IEnumerable<SourceTable>? sourceTables)
    {
        if (sourceTables == null)
        {
            return new List<SourceTableDto>();
        }

        return sourceTables
            .Select(source => new SourceTableDto
            {
                Kind = source.Kind.ToString(),
                Database = source.Database,
                Schema = source.Schema,
                Table = source.Table,
                Alias = source.Alias,
                ParentAlias = source.ParentAlias,
                JoinType = source.JoinType,
                ResolvedOrder = source.ResolvedOrder,
                JoinExpression = source.JoinExpression,
                IsBaseTable = source.Kind == SourceKind.NamedObject,
                JoinKeys = source.JoinKeys?
                    .Select(key => new JoinKeyDto
                    {
                        LocalColumn = key.LocalColumn,
                        RemoteExpression = key.RemoteExpression,
                        Cardinality = key.Cardinality.ToString()
                    })
                    .ToList() ?? new List<JoinKeyDto>()
            })
            .ToList();
    }

    public static List<ViewColumnDto> ToViewColumnDtos(this IEnumerable<ViewSourcedColumnDefinition>? columns)
    {
        if (columns == null)
        {
            return new List<ViewColumnDto>();
        }

        return columns
            .Select(column => new ViewColumnDto
            {
                ColumnId = column.ColumnId,
                TableId = column.TableId,
                OrdinalPosition = column.OrdinalPosition,
                ColumnName = column.ColumnName,
                Database = column.Database,
                Schema = column.Schema,
                Table = column.Table,
                ColumnKind = column.ColumnKind.ToString(),
                BaseDatabase = column.BaseDatabase,
                BaseSchema = column.BaseSchema,
                BaseTable = column.BaseTable,
                BaseColumn = column.BaseColumn,
                SemanticDatabase = column.SemanticDatabase,
                SemanticSchema = column.SemanticSchema,
                SemanticObject = column.SemanticObject,
                SemanticColumn = column.SemanticColumn,
                BusinessName = column.BusinessName,
                BusinessDescription = column.BusinessDescription,
                DeveloperNotes = column.DeveloperNotes,
                Comment = column.Comment,
                LastSynced = column.LastSynced,
                IsDirty = column.isDirty
            })
            .ToList();
    }
}
