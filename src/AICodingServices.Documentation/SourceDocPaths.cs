using System;
using System.IO;

namespace AICodingServices.Documentation
{
    public static class SourceDocPaths
    {
        public const string DocsFolderName = "Docs";
        public const string FolderDocFileName = "Folder.aim.md";
        public const string ManifestFileName = "manifest.aim.json";

        public static string GetFolderDocsPath(string selectedFolderPath)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                throw new ArgumentException("Selected folder path is required.", nameof(selectedFolderPath));
            }

            return Path.Combine(selectedFolderPath, DocsFolderName);
        }

        public static string GetSourceFileDocPath(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                throw new ArgumentException("Source file path is required.", nameof(sourceFilePath));
            }

            string? folderPath = Path.GetDirectoryName(sourceFilePath);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentException("Source file path must include a folder.", nameof(sourceFilePath));
            }

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourceFilePath);
            return Path.Combine(folderPath, DocsFolderName, fileNameWithoutExtension + ".aim.md");
        }
    }
}
