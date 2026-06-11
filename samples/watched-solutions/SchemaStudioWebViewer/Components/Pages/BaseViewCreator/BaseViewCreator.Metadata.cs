using System.Reflection;
using Microsoft.AspNetCore.Components;
using Radzen.Blazor;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Data;
using SchemaStudioWebViewer.Models;
using SchemaStudioWebViewer.Utils;

namespace SchemaStudioWebViewer.Components.Pages.BaseViewCreator;

public sealed record BaseViewCreatorMetadataEditResult(string BusinessName, string BusinessDescription);

public partial class BaseViewCreator
{
    private void OpenColumnMetadataDialog(TableSchemaColumnInfo column)
    {
        MetadataEditColumn = column;
        MetadataEditRelationship = null;
        MetadataDialogTitle = $"Metadata for {column.ColumnName}";
        MetadataEditBusinessName = column.BusinessName;
        MetadataEditBusinessDescription = column.BusinessDescription;
        MetadataDialogOpen = true;
    }

    private void OpenRelationshipDisplayMetadataDialog(TableSchemaRelationshipInfo relationship)
    {
        MetadataEditColumn = null;
        MetadataEditRelationship = relationship;
        MetadataDialogTitle = $"Metadata for {BuildLookupProjectionAlias(relationship)}";
        MetadataEditBusinessName = relationship.DisplayBusinessName;
        MetadataEditBusinessDescription = relationship.DisplayBusinessDescription;
        MetadataDialogOpen = true;
    }

    private void CloseMetadataDialog()
    {
        MetadataDialogOpen = false;
        MetadataEditColumn = null;
        MetadataEditRelationship = null;
    }

    private void ApplyMetadataDialog(BaseViewCreatorMetadataEditResult editResult)
    {
        if (!ValidateMetadataDialog(editResult))
        {
            return;
        }

        MetadataEditBusinessName = editResult.BusinessName;
        MetadataEditBusinessDescription = editResult.BusinessDescription;

        if (MetadataEditColumn is not null)
        {
            MetadataEditColumn.BusinessName = MetadataEditBusinessName;
            MetadataEditColumn.BusinessDescription = MetadataEditBusinessDescription;
        }
        else if (MetadataEditRelationship is not null)
        {
            MetadataEditRelationship.DisplayBusinessName = MetadataEditBusinessName;
            MetadataEditRelationship.DisplayBusinessDescription = MetadataEditBusinessDescription;
        }

        MarkSqlDirty();
        CloseMetadataDialog();
    }

    private bool ValidateMetadataDialog(BaseViewCreatorMetadataEditResult editResult)
    {
        if (editResult.BusinessName.Length > MetadataBusinessNameMaxLength)
        {
            NotifyError($"Business Name is {editResult.BusinessName.Length} characters; max is {MetadataBusinessNameMaxLength}.");
            return false;
        }

        if (editResult.BusinessDescription.Length > MetadataBusinessDescriptionMaxLength)
        {
            NotifyError($"Business Description is {editResult.BusinessDescription.Length} characters; max is {MetadataBusinessDescriptionMaxLength}.");
            return false;
        }

        return true;
    }

    private string BuildMetadataPlaceholder(string prefix, string projection, string outputColumnName, string businessName = "", string businessDescription = "", bool disableInheritance = false)
    {
        if (disableInheritance)
        {
            businessName = string.IsNullOrWhiteSpace(businessName) ? outputColumnName : businessName;
            businessDescription = string.IsNullOrWhiteSpace(businessDescription) ? outputColumnName : businessDescription;
        }

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(businessName))
        {
            parts.Add($"@BusinessName: {businessName}");
        }

        if (!string.IsNullOrWhiteSpace(businessDescription))
        {
            parts.Add($"@BusinessDescription: {businessDescription}");
        }

        if (disableInheritance)
        {
            parts.Add("@DisableInheritance: True");
        }

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        return BuildMultilineMetadataPlaceholder(prefix, projection, parts);
    }

    private static string BuildMultilineMetadataPlaceholder(string prefix, string projection, IReadOnlyList<string> parts)
    {
        var projectionLeadLength = prefix.Length + projection.Length;
        var tagIndent = new string(' ', projectionLeadLength + 4);
        var closeIndent = new string(' ', projectionLeadLength + 1);
        var builder = new System.Text.StringBuilder();
        builder.Append($" /* {parts[0]}");

        for (var index = 1; index < parts.Count; index++)
        {
            builder.AppendLine();
            builder.Append(tagIndent);
            builder.Append(parts[index]);
        }

        builder.AppendLine();
        builder.Append(closeIndent);
        builder.Append("*/");
        return builder.ToString();
    }

    private RenderFragment MetadataFieldLabel(string propertyName) => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "bvg-field-label");
        builder.AddContent(2, ReflectionUtils.GetDisplayName(typeof(SchemaObjectColumnDefinition), propertyName));
        builder.AddContent(3, MetadataHelp(typeof(SchemaObjectColumnDefinition), propertyName));
        builder.CloseElement();
    };

    private RenderFragment MetadataHelp(Type modelType, string propertyName) => builder =>
    {
        var description = ReflectionUtils.GetDescription(modelType, propertyName);
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        builder.OpenComponent<RadzenIcon>(0);
        builder.AddAttribute(1, "Icon", "help_outline");
        builder.AddAttribute(2, "class", "bvg-help-icon");
        builder.AddAttribute(3, "MouseEnter", EventCallback.Factory.Create<ElementReference>(this, args => TooltipService.Open(args, description)));
        builder.CloseComponent();
    };

    private RenderFragment MetadataSummary(string? businessName, string? businessDescription) => builder =>
    {
        var hasName = !string.IsNullOrWhiteSpace(businessName);
        var hasDescription = !string.IsNullOrWhiteSpace(businessDescription);

        builder.OpenElement(1, "span");
        builder.AddAttribute(2, "class", hasName || hasDescription ? "bvg-metadata-summary" : "bvg-metadata-summary empty");

        if (hasName)
        {
            builder.OpenElement(3, "span");
            builder.AddContent(4, businessName);
            builder.CloseElement();
        }

        if (hasDescription)
        {
            builder.OpenElement(5, "span");
            builder.AddContent(6, businessDescription);
            builder.CloseElement();
        }

        if (!hasName && !hasDescription)
        {
            builder.AddContent(7, "No metadata");
        }

        builder.CloseElement();
    };
}
