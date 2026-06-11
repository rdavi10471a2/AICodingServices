using System.ComponentModel;
using ModelContextProtocol.Server;
using SchemaStudio.AIHelpers;
using SchemaStudioWebViewer.Data;
using SchemaStudioWebViewer.Models;

namespace SchemaStudioWebViewer.McpTools;

[McpServerToolType]
[FileVersion("1.5")]
[AIFileContext("McpTools/SchemaCatalogMcpTools.cs", "MCP tool surface for read-only Schema Studio catalog discovery. Wraps SchemaMCPRepository calls in structured success/error objects for AI callers.", Responsibilities = "Exposes chainable MCP tools for listing databases, domains, schema objects, object fields, and focused object/field descriptions.", Nuances = "Keep repository exceptions contained here so MCP callers receive recoverable JSON instead of transport-level failures for normal lookup mistakes.", LastReviewed = "2026-05-07")]
public sealed class SchemaCatalogMcpTools
{
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 250;

    private readonly SchemaMCPRepository repository;

    public SchemaCatalogMcpTools(SchemaMCPRepository repository)
    {
        this.repository = repository;
    }

    [McpServerTool(Name = "schema_list_databases")]
    [Description("List active Schema Studio databases. Use this before asking for domains or schema objects.")]
    public async Task<object> ListDatabasesAsync(
        [Description("When true, include suggested next tool calls in the response. Defaults to true.")] bool? includeNext = null,
        [Description("Maximum number of rows to return. Defaults to 100 and caps at 250.")] int? top = null,
        [Description("Optional case-insensitive text search over database names and descriptions.")] string? search = null)
    {
        try
        {
            var showNext = ResolveIncludeNext(includeNext);
            var limit = ResolveListLimit(top);
            var databases = await repository.GetDatabasesAsync(limit, search);
            return Ok(new
            {
                resultLimit = limit,
                resultCount = databases.Count,
                search,
                databases = databases.Select(x => ProjectDatabase(x, showNext)).ToList()
            });
        }
        catch (Exception ex)
        {
            return Fail("database_list_failed", "Failed to list Schema Studio databases.", ex);
        }
    }

    [McpServerTool(Name = "schema_list_domains")]
    [Description("List business domains for an active Schema Studio database.")]
    public async Task<object> ListDomainsAsync(
        [Description("Schema Studio database id returned by schema_list_databases.")] int databaseId,
        [Description("When true, include suggested next tool calls in the response. Defaults to true.")] bool? includeNext = null,
        [Description("Maximum number of rows to return. Defaults to 100 and caps at 250.")] int? top = null,
        [Description("Optional case-insensitive text search over domain names.")] string? search = null)
    {
        try
        {
            var showNext = ResolveIncludeNext(includeNext);
            var database = await repository.GetDatabaseAsync(databaseId);
            if (database is null)
            {
                return Fail("database_not_found", $"Database id {databaseId} was not found or is inactive.");
            }

            var limit = ResolveListLimit(top);
            var domains = await repository.GetDomainsAsync(databaseId, limit, search);
            return Ok(new
            {
                database = ProjectDatabase(database, showNext),
                resultLimit = limit,
                resultCount = domains.Count,
                search,
                domains = domains.Select(x => new
                {
                    x.DatabaseDomainId,
                    x.DatabaseId,
                    x.Domain,
                    next = showNext ? new
                    {
                        listObjects = new
                        {
                            tool = "schema_list_objects",
                            arguments = new
                            {
                                databaseId = x.DatabaseId,
                                domain = x.Domain
                            }
                        }
                    } : null
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            return Fail("domain_list_failed", $"Failed to list domains for database id {databaseId}.", ex);
        }
    }

    [McpServerTool(Name = "schema_list_objects")]
    [Description("List active schema objects for a database, optionally filtered to one domain.")]
    public async Task<object> ListSchemaObjectsAsync(
        [Description("Schema Studio database id returned by schema_list_databases.")] int databaseId,
        [Description("Optional domain returned by schema_list_domains. Leave null to list all domains.")] string? domain = null,
        [Description("When true, include suggested next tool calls in the response. Defaults to true.")] bool? includeNext = null,
        [Description("Maximum number of rows to return. Defaults to 100 and caps at 250.")] int? top = null,
        [Description("Optional case-insensitive text search over object names and descriptions.")] string? search = null)
    {
        try
        {
            var showNext = ResolveIncludeNext(includeNext);
            var limit = ResolveListLimit(top);
            var listResult = await repository.GetSchemaObjectListAsync(databaseId, domain, limit, search);
            var database = listResult.Database;
            if (database is null)
            {
                return Fail("database_not_found", $"Database id {databaseId} was not found or is inactive.");
            }

            if (!string.IsNullOrWhiteSpace(domain) && !listResult.DomainFound)
            {
                return Fail(
                    "domain_not_found",
                    $"Domain '{domain}' was not found for database id {databaseId}.");
            }

            return Ok(new
            {
                database = ProjectDatabase(database, showNext),
                domain = listResult.Domain,
                resultLimit = limit,
                resultCount = listResult.Objects.Count,
                search,
                objects = listResult.Objects.Select(x => ProjectSchemaObject(x, showNext)).ToList()
            });
        }
        catch (Exception ex)
        {
            return Fail("object_list_failed", $"Failed to list schema objects for database id {databaseId}.", ex);
        }
    }

    [McpServerTool(Name = "schema_describe_object")]
    [Description("Describe one Schema Studio object by schemaObjectId.")]
    public async Task<object> DescribeSchemaObjectAsync(
        [Description("Schema object id returned by schema_list_objects.")] int schemaObjectId,
        [Description("When true, include suggested next tool calls in the response. Defaults to true.")] bool? includeNext = null)
    {
        try
        {
            var showNext = ResolveIncludeNext(includeNext);
            var schemaObject = await repository.GetSchemaObjectAsync(schemaObjectId);
            if (schemaObject is null)
            {
                return Fail("object_not_found", $"Schema object id {schemaObjectId} was not found or is inactive.");
            }

            return Ok(ProjectSchemaObject(schemaObject, showNext));
        }
        catch (Exception ex)
        {
            return Fail("object_describe_failed", $"Failed to describe schema object id {schemaObjectId}.", ex);
        }
    }

    [McpServerTool(Name = "schema_get_view_sql")]
    [Description("Get the SQL definition text for one Schema Studio view/object. Use this when an AI needs an example query shape before rewriting or generating a query.")]
    public async Task<object> GetViewSqlAsync(
        [Description("Schema object id returned by schema_list_objects.")] int schemaObjectId,
        [Description("When true, remove Schema Studio parser metadata comment blocks from the returned SQL.")] bool cleanMetadataComments = false,
        [Description("When true, include suggested next tool calls in the response. Defaults to true.")] bool? includeNext = null)
    {
        try
        {
            var showNext = ResolveIncludeNext(includeNext);
            var schemaObject = await repository.GetSchemaObjectAsync(schemaObjectId);
            if (schemaObject is null)
            {
                return Fail("object_not_found", $"Schema object id {schemaObjectId} was not found or is inactive.");
            }

            var viewSql = await repository.GetViewSqlAsync(schemaObject);
            if (viewSql is null || string.IsNullOrWhiteSpace(viewSql.Definition))
            {
                return Fail(
                    "view_sql_not_found",
                    $"SQL definition was not found for schema object id {schemaObjectId}.",
                    details: ProjectSchemaObject(schemaObject, showNext));
            }

            var sqlText = cleanMetadataComments
                ? SchemaMCPRepository.CleanSqlDefinition(viewSql.Definition)
                : viewSql.Definition;

            return Ok(new
            {
                schemaObject = ProjectSchemaObject(schemaObject, showNext),
                cleanMetadataComments,
                modifyDate = viewSql.ModifyDate,
                sql = sqlText
            });
        }
        catch (Exception ex)
        {
            return Fail("view_sql_failed", $"Failed to get view SQL for schema object id {schemaObjectId}.", ex);
        }
    }

    [McpServerTool(Name = "schema_list_fields")]
    [Description("List fields/columns for one Schema Studio object.")]
    public async Task<object> ListFieldsAsync(
        [Description("Schema object id returned by schema_list_objects.")] int schemaObjectId,
        [Description("When true, include suggested next tool calls in the response. Defaults to true.")] bool? includeNext = null,
        [Description("Maximum number of rows to return. Defaults to 100 and caps at 250.")] int? top = null,
        [Description("Optional case-insensitive text search over field names and descriptions.")] string? search = null)
    {
        try
        {
            var showNext = ResolveIncludeNext(includeNext);
            var schemaObject = await repository.GetSchemaObjectAsync(schemaObjectId);
            if (schemaObject is null)
            {
                return Fail("object_not_found", $"Schema object id {schemaObjectId} was not found or is inactive.");
            }

            var limit = ResolveListLimit(top);
            var fields = await repository.GetFieldsAsync(schemaObjectId, limit, search);
            return Ok(new
            {
                schemaObject = ProjectSchemaObject(schemaObject, showNext),
                resultLimit = limit,
                resultCount = fields.Count,
                search,
                fields = fields.Select(x => ProjectField(x, showNext)).ToList()
            });
        }
        catch (Exception ex)
        {
            return Fail("field_list_failed", $"Failed to list fields for schema object id {schemaObjectId}.", ex);
        }
    }

    [McpServerTool(Name = "schema_describe_field")]
    [Description("Describe one Schema Studio field by schemaObjectColumnId.")]
    public async Task<object> DescribeFieldAsync(
        [Description("Schema object column id returned by schema_list_fields.")] int schemaObjectColumnId,
        [Description("When true, include suggested next tool calls in the response. Defaults to true.")] bool? includeNext = null)
    {
        try
        {
            var showNext = ResolveIncludeNext(includeNext);
            var field = await repository.GetFieldAsync(schemaObjectColumnId);
            if (field is null)
            {
                return Fail("field_not_found", $"Schema object column id {schemaObjectColumnId} was not found.");
            }

            return Ok(ProjectField(field, showNext));
        }
        catch (Exception ex)
        {
            return Fail("field_describe_failed", $"Failed to describe schema object column id {schemaObjectColumnId}.", ex);
        }
    }

    private static object Ok(object data) => new
    {
        ok = true,
        data,
        error = (object?)null
    };

    private static object Fail(string code, string message, Exception? exception = null, object? details = null)
    {
#if DEBUG
        return new
        {
            ok = false,
            data = (object?)null,
            error = new
            {
                code,
                message,
                exceptionType = exception?.GetType().Name,
                exceptionMessage = exception?.Message,
                details
            }
        };
#else
        return new
        {
            ok = false,
            data = (object?)null,
            error = new
            {
                code,
                message,
                details
            }
        };
#endif
    }

    private static bool ResolveIncludeNext(bool? includeNext) =>
        includeNext ?? true;

    private static int ResolveListLimit(int? top)
    {
        if (!top.HasValue)
        {
            return DefaultListLimit;
        }

        return Math.Clamp(top.Value, 1, MaxListLimit);
    }

    private static object ProjectDatabase(DatabaseModel database, bool includeNext) => new
    {
        database.DatabaseId,
        database.DatabaseName,
        database.DefaultSchema,
        database.BusinessName,
        database.BusinessDescription,
        database.ViewNameFilter,
        next = includeNext ? new
        {
            listDomains = new
            {
                tool = "schema_list_domains",
                arguments = new
                {
                    databaseId = database.DatabaseId
                }
            },
            listObjects = new
            {
                tool = "schema_list_objects",
                arguments = new
                {
                    databaseId = database.DatabaseId
                }
            }
        } : null
    };

    private static object ProjectSchemaObject(SchemaObjectModel schemaObject, bool includeNext) => new
    {
        schemaObject.SchemaObjectId,
        schemaObject.DatabaseId,
        schemaObject.SourceDatabaseName,
        schemaObject.SourceSchemaName,
        schemaObject.SourceObjectName,
        sourceName = schemaObject.SourceName,
        schemaObject.BusinessName,
        schemaObject.BusinessDescription,
        schemaObject.DeveloperNotes,
        schemaObject.CompositionDefinitionJson,
        schemaObject.IsBaseObject,
        schemaObject.Domain,
        schemaObject.LastSynced,
        next = includeNext ? new
        {
            describeObject = new
            {
                tool = "schema_describe_object",
                arguments = new
                {
                    schemaObjectId = schemaObject.SchemaObjectId
                }
            },
            listFields = new
            {
                tool = "schema_list_fields",
                arguments = new
                {
                    schemaObjectId = schemaObject.SchemaObjectId
                }
            },
            getViewSql = new
            {
                tool = "schema_get_view_sql",
                arguments = new
                {
                    schemaObjectId = schemaObject.SchemaObjectId
                }
            }
        } : null
    };

    private static object ProjectField(SchemaObjectColumnModel field, bool includeNext) => new
    {
        field.SchemaObjectColumnId,
        field.SchemaObjectId,
        field.OrdinalPosition,
        field.SourceColumnName,
        field.SourceColumnKind,
        field.BaseDatabaseName,
        field.BaseSchemaName,
        field.BaseObjectName,
        field.BaseColumnName,
        field.FullyQualifiedSourceColumnName,
        field.IsBaseDefinition,
        field.DisableInheritance,
        field.BusinessName,
        field.BusinessDescription,
        field.DeveloperNotes,
        field.LastSynced,
        next = includeNext ? new
        {
            describeField = new
            {
                tool = "schema_describe_field",
                arguments = new
                {
                    schemaObjectColumnId = field.SchemaObjectColumnId
                }
            }
        } : null
    };
}
