using SchemaStudioWebViewer.WEBSemanticModel.Model;
using SchemaStudio.AIHelpers;
using System.Text.RegularExpressions;

namespace SchemaStudioWebViewer.WEBSemanticModel.Parsing
{
    [FileVersion("1.0")]
    [AIFileContext("WEBSemanticModel/Parsing/ViewMetaDataBinder.cs", "Extracts parser-owned metadata tags from SQL comments and applies them to parsed select items before export.", Responsibilities = "Owns BusinessName, BusinessDescription, and DisableInheritance parser tags while intentionally leaving developer notes user-owned in the web workflow.", Nuances = "Do not reintroduce parser ownership for user-owned metadata such as DeveloperNotes without an explicit workflow change.", RelatedFiles = "ParsedQuery, SelectItem, ViewSourcedColumnDefinition", LastReviewed = "2026-04-25")]
    [AIChange("1.0", "2026-04-25 04:28 PM CDT added parser binding for @DisableInheritance so semantic lookup overrides can flow through the parsed column pipeline without bringing DeveloperNotes back under parser ownership.", AICommandStatus.Pending)]
    public static class ViewMetadataBinder
    {
        // 2026-04-25 04:28 PM CDT AI v1.0 marker: parser comments can now opt a column out of inherited metadata by emitting @DisableInheritance.
        // Singleline allows the dot (.) to match newlines within the comment block
        private const RegexOptions CommonOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline;

        private static readonly Regex NameRegex = new Regex(@"@BusinessName[:\s]*(?<val>.*?)(?=@|$)", CommonOptions);
        private static readonly Regex DescRegex = new Regex(@"@BusinessDescription[:\s]*(?<val>.*?)(?=@|$)", CommonOptions);
        private static readonly Regex DisableInheritanceRegex = new Regex(@"@DisableInheritance[:\s]*(?<val>.*?)(?=@|$)", CommonOptions);
        // private static readonly Regex NoteRegex = new Regex(@"@DeveloperNotes[:\s]*(?<val>.*?)(?=@|$)", CommonOptions);

        public static void Apply(ParsedQuery query)
        {
            if (query == null) return;

            foreach (var item in query.SelectItems)
            {
                if (string.IsNullOrWhiteSpace(item.Comment)) continue;

                // 1. Clean the outer shell of the SQL comment
                // This removes --, /*, and importantly, the trailing */
                string cleanComment = item.Comment.Trim()
                    .TrimStart('-', '/', '*')
                    .TrimEnd('*', '/', ' ')
                    .Trim();

                item.BusinessName = ExtractTag(cleanComment, NameRegex);
                item.BusinessDescription = ExtractTag(cleanComment, DescRegex);
                item.DisableInheritance = ExtractBoolTag(cleanComment, DisableInheritanceRegex);
                // Developer notes are user-owned in the web workflow now, so leave parser-derived notes blank.
                // item.DeveloperNotes = ExtractTag(cleanComment, NoteRegex);
            }
        }

        private static string ExtractTag(string input, Regex regex)
        {
            var match = regex.Match(input);
            if (match.Success)
            {
                // The Triple-Trim Strategy:
                // 1. Outer Trim: Removes spaces/newlines captured by the lazy dot-match
                // 2. TrimStart(':'): Specifically targets the colon separator
                // 3. Final Trim: Ensures the resulting data has no leading/trailing junk
                return match.Groups["val"].Value
                    .Trim()
                    .TrimStart(':')
                    .Trim();
            }
            return null;
        }

        private static bool ExtractBoolTag(string input, Regex regex)
        {
            var value = ExtractTag(input, regex);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "1" or "true" or "yes" or "y" or "on" => true,
                _ => false
            };
        }
    }
}
