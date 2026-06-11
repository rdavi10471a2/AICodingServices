using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace SchemaStudioWebViewer.Utils
{
    public static class ReflectionUtils
    {
        /// <summary>
        /// Retrieves the string value of the [Description] attribute for a given property.
        /// </summary>
        public static string GetDescription<T>(string propertyName)
        {
            return GetDescription(typeof(T), propertyName);
        }

        /// <summary>
        /// Overload for when you have the Type object directly.
        /// </summary>
        public static string GetDescription(Type type, string propertyName)
        {
            var prop = type.GetProperty(propertyName);
            if (prop == null) return string.Empty;

            return prop.GetCustomAttribute<DescriptionAttribute>()?.Description
                   ?? prop.GetCustomAttribute<DisplayAttribute>()?.Description
                   ?? string.Empty;
        }

        /// <summary>
        /// Retrieves the Display Name from the [Display] attribute.
        /// Falls back to the Property Name if not found.
        /// </summary>
        public static string GetDisplayName<T>(string propertyName)
        {
            return GetDisplayName(typeof(T), propertyName);
        }

        /// <summary>
        /// Overload for when you have the Type object directly.
        /// </summary>
        public static string GetDisplayName(Type type, string propertyName)
        {
            var prop = type.GetProperty(propertyName);
            if (prop == null) return propertyName;

            return prop.GetCustomAttribute<DisplayAttribute>()?.Name
                   ?? propertyName;
        }

        /// <summary>
        /// Checks if the property is marked with [MultilineDisplayRequired(true)]
        /// </summary>
        public static bool IsMultilineRequired<T>(string propertyName)
        {
            return IsMultilineRequired(typeof(T), propertyName);
        }

        /// <summary>
        /// Overload for when you have the Type object directly.
        /// </summary>
        public static bool IsMultilineRequired(Type type, string propertyName)
        {
            var prop = type.GetProperty(propertyName);
            if (prop == null) return false;

            // Look for your custom attribute specifically in the Models namespace
            var attr = prop.GetCustomAttribute<Models.MultilineDisplayRequiredAttribute>();
            if (attr?.IsMultiline == true) return true;

            var dataType = prop.GetCustomAttribute<DataTypeAttribute>();
            return dataType?.DataType == DataType.MultilineText;
        }

        public static bool IsDetailViewOnly<T>(string propertyName)
        {
            return IsDetailViewOnly(typeof(T), propertyName);
        }

        public static bool IsDetailViewOnly(Type type, string propertyName)
        {
            var prop = type.GetProperty(propertyName);
            if (prop == null) return false;

            var attr = prop.GetCustomAttribute<Models.DetailViewOnlyAttribute>();

            return attr?.IsDetailOnly ?? false;
        }
    }
}
