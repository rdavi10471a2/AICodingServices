using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.WEBSemanticModel.Diagnostics;
using SchemaStudioWebViewer.WEBSemanticModel.Model;
using SchemaStudioWebViewer.WEBSemanticModel.Orchestration;
using SchemaStudioWebViewer.WEBSemanticModel.Providers;

namespace SchemaStudioWebViewer.WEBSemanticModel.Services
{
    public class ViewParsingService
    {
        private readonly IViewDefinitionProvider _provider;
        private readonly IQueryLogger _logger;

        public ViewParsingService(string connectionString, IQueryLogger logger = null)
        {
            _logger = logger ?? new NullQueryLogger();
            _provider = new ViewDefinitionProvider(connectionString, _logger);

            _logger.Info("ViewParsingService initialized");
        }

        public ViewParsingService(IViewDefinitionProvider provider, IQueryLogger logger = null)
        {
            _logger = logger ?? new NullQueryLogger();
            _provider = provider;

            _logger.Info("ViewParsingService initialized (external provider)");
        }

        public string GetViewSql(string database, string schema, string viewName)
        {
            _logger.Info($"GetViewSql: {database}.{schema}.{viewName}");

            try
            {
                var sql = _provider.GetViewDefinition(database, schema, viewName);

                if (string.IsNullOrWhiteSpace(sql))
                {
                    _logger.Warning($"View SQL not found: {database}.{schema}.{viewName}");
                }
                else
                {
                    _logger.Info($"SQL retrieved ({sql.Length} chars)");
                }

                return sql;
            }
            catch (Exception ex)
            {
                _logger.Error($"FAILED GetViewSql: {database}.{schema}.{viewName}", ex);
                throw;
            }
        }

        public ParsedQuery ParseView(string database, string schema, string viewName)
        {
            _logger.Info($"ParseView START: {database}.{schema}.{viewName}");

            try
            {
                var sql = GetViewSql(database, schema, viewName);

                if (string.IsNullOrWhiteSpace(sql))
                {
                    throw new Exception($"View not found: {database}.{schema}.{viewName}");
                }

                _logger.Info("Calling QueryOrchestrator.ParseFully");

                var result = QueryOrchestrator.ParseFully(
                    sql,
                    database,
                    schema,
                    viewName,
                    _provider,
                    _logger);

                if (result == null)
                {
                    _logger.Warning("ParseFully returned null");
                    return new ParsedQuery();
                }

                _logger.Info($"ParseFully complete: {result.SourceTables.Count} tables, {result.SelectItems.Count} select items");
                _logger.Info("Projecting columns");

                result.Columns = result.ToColumns(database, schema, viewName);

                _logger.Info($"Columns projected: {result.Columns?.Count ?? 0}");
                _logger.Info($"ParseView COMPLETE: {database}.{schema}.{viewName}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"FAILED ParseView: {database}.{schema}.{viewName}", ex);
                throw;
            }
        }

        public List<SourceTableDto> GetSourceTableDtos(string database, string schema, string viewName)
        {
            var parsed = ParseView(database, schema, viewName);
            return parsed?.SourceTables.ToSourceTableDtos() ?? new List<SourceTableDto>();
        }

        public List<ViewColumnDto> GetViewColumnDtos(string database, string schema, string viewName)
        {
            var parsed = ParseView(database, schema, viewName);
            return parsed?.Columns.ToViewColumnDtos() ?? new List<ViewColumnDto>();
        }

        public ParsedQuery ReloadView(string database, string schema, string viewName)
        {
            _logger.Info($"ReloadView: {database}.{schema}.{viewName}");

            try
            {
                _provider.Reload(database, schema, viewName);
                return ParseView(database, schema, viewName);
            }
            catch (Exception ex)
            {
                _logger.Error($"FAILED ReloadView: {database}.{schema}.{viewName}", ex);
                throw;
            }
        }

        public List<SourceTableDto> ReloadSourceTableDtos(string database, string schema, string viewName)
        {
            var parsed = ReloadView(database, schema, viewName);
            return parsed?.SourceTables.ToSourceTableDtos() ?? new List<SourceTableDto>();
        }

        public List<ViewColumnDto> ReloadViewColumnDtos(string database, string schema, string viewName)
        {
            var parsed = ReloadView(database, schema, viewName);
            return parsed?.Columns.ToViewColumnDtos() ?? new List<ViewColumnDto>();
        }

        public ParsedQuery ReloadViewChain(string database, string schema, string viewName)
        {
            _logger.Info($"ReloadViewChain: {database}.{schema}.{viewName}");

            try
            {
                _provider.ReloadChain(database, schema, viewName);
                return ParseView(database, schema, viewName);
            }
            catch (Exception ex)
            {
                _logger.Error($"FAILED ReloadViewChain: {database}.{schema}.{viewName}", ex);
                throw;
            }
        }

        public List<SourceTableDto> ReloadChainSourceTableDtos(string database, string schema, string viewName)
        {
            var parsed = ReloadViewChain(database, schema, viewName);
            return parsed?.SourceTables.ToSourceTableDtos() ?? new List<SourceTableDto>();
        }

        public List<ViewColumnDto> ReloadChainViewColumnDtos(string database, string schema, string viewName)
        {
            var parsed = ReloadViewChain(database, schema, viewName);
            return parsed?.Columns.ToViewColumnDtos() ?? new List<ViewColumnDto>();
        }

        public void ClearCache()
        {
            _logger.Info("ClearCache");

            try
            {
                _provider.ClearCache();
                _logger.Info("Cache cleared");
            }
            catch (Exception ex)
            {
                _logger.Error("FAILED ClearCache", ex);
                throw;
            }
        }

        public List<string> GetViews(string database, string schema)
        {
            _logger.Info($"GetViews Function: {database}.{schema}");

            try
            {
                var views = _provider.GetViews(database, schema);

                _logger.Info($"Views Function found : {views.Count}");

                return views
                    .OrderBy(x => x)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"FAILED GetViews: {database}.{schema}", ex);
                throw;
            }
        }
    }
}
