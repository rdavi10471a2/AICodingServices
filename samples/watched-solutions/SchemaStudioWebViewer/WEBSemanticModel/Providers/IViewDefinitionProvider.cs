namespace SchemaStudioWebViewer.WEBSemanticModel.Providers
{
    public interface IViewDefinitionProvider
    {
        string GetViewDefinition(string database, string schema, string viewName);
        void Reload(string database, string schema, string viewName);
        void ReloadChain(string database, string schema, string viewName);
        void ClearCache();
        List<string> GetViews(string database, string schema);
    }
}
