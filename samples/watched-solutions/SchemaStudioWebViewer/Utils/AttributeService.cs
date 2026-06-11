namespace SchemaStudioWebViewer.Utils
{
    using SchemaStudioWebViewer.Models;
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Reflection;

    public class AttributeService
    {
        private readonly ConcurrentDictionary<string, string> _cache = new();
        private readonly ConcurrentDictionary<string, Type?> _typeCache = new();

        // Keep the generic versions for code-behind usage
        public string Name<T>(string prop) => Get(typeof(T).Name, prop, "Name", () => ReflectionUtils.GetDisplayName<T>(prop));
        public string Description<T>(string prop) => Get(typeof(T).Name, prop, "Desc", () => ReflectionUtils.GetDescription<T>(prop));

        // String-based "sort it out" methods
        public string GetName(string qualifiedName) => ResolveFromPath(qualifiedName, "Name");
        public string GetDesc(string qualifiedName) => ResolveFromPath(qualifiedName, "Desc");

        private string ResolveFromPath(string path, string suffix)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.Contains('.'))
                return path;

            var key = $"{path}.{suffix}";

            return _cache.GetOrAdd(key, _ =>
            {
                var parts = path.Split('.');
                var typeName = parts[0];
                var propName = parts[1];

                // Cache type-name resolution so shared DTO metadata lookups stay cheap after
                // the first hit and we do not rescan loaded assemblies for every property read.
                var type = _typeCache.GetOrAdd(typeName, ResolveTypeByName);

                if (type != null)
                {
                    return suffix == "Name"
                        ? ReflectionUtils.GetDisplayName(type, propName)
                        : ReflectionUtils.GetDescription(type, propName);
                }

                return propName;
            });
        }

        private string Get(string type, string prop, string suffix, Func<string> fetcher)
        {
            var key = $"{type}.{prop}.{suffix}";
            return _cache.GetOrAdd(key, _ => fetcher() ?? "");
        }

        private static Type? ResolveTypeByName(string typeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(SafeGetTypes)
                .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
        }
    }
}
