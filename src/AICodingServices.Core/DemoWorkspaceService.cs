using System.Text.Json;

namespace AICodingServices.Core;

public sealed class DemoWorkspaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly MonitorSettings settings;

    public DemoWorkspaceService(MonitorSettings settings)
    {
        this.settings = settings;
    }

    public string DemoRoot => Path.Combine(settings.RuntimeRoot, "demos");

    public DemoWorkspaceWriteResult WriteDemoFile(
        string relativePath,
        string content,
        string? purpose = null,
        string? author = null,
        IReadOnlyList<string>? relatedSourcePaths = null)
    {
        string normalizedRelativePath = NormalizeRelativePath(relativePath);
        string fullPath = ResolveDemoPath(normalizedRelativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        DemoWorkspaceFileMetadata metadata = new(
            normalizedRelativePath,
            fullPath,
            string.IsNullOrWhiteSpace(purpose) ? "Local demo or proposal file." : purpose.Trim(),
            string.IsNullOrWhiteSpace(author) ? "agent" : author.Trim(),
            DateTimeOffset.UtcNow,
            relatedSourcePaths?.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim()).ToArray() ?? []);
        File.WriteAllText(GetMetadataPath(fullPath), JsonSerializer.Serialize(metadata, JsonOptions));

        FileInfo fileInfo = new(fullPath);
        return new DemoWorkspaceWriteResult(
            metadata.RelativePath,
            metadata.FullPath,
            metadata.MetadataPath,
            fileInfo.Length,
            metadata.UpdatedAtUtc,
            "Demo file was written under runtime/demos. It is not watched source and is not staged for review.");
    }

    public DemoWorkspaceDeleteResult DeleteDemoFile(string relativePath)
    {
        string normalizedRelativePath = NormalizeRelativePath(relativePath);
        string fullPath = ResolveDemoPath(normalizedRelativePath);
        string metadataPath = GetMetadataPath(fullPath);
        bool fileDeleted = false;
        bool metadataDeleted = false;

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            fileDeleted = true;
        }

        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
            metadataDeleted = true;
        }

        return new DemoWorkspaceDeleteResult(
            normalizedRelativePath,
            fullPath,
            metadataPath,
            fileDeleted,
            metadataDeleted,
            fileDeleted ? "Demo file was deleted." : "Demo file did not exist; metadata cleanup was attempted.");
    }

    public DemoWorkspaceFile ReadDemoFile(string relativePath)
    {
        string normalizedRelativePath = NormalizeRelativePath(relativePath);
        string fullPath = ResolveDemoPath(normalizedRelativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Demo file was not found.", fullPath);
        }

        string content = File.ReadAllText(fullPath);
        DemoWorkspaceFileMetadata metadata = ReadMetadata(fullPath, normalizedRelativePath);
        return new DemoWorkspaceFile(
            normalizedRelativePath,
            fullPath,
            metadata.Purpose,
            metadata.Author,
            metadata.UpdatedAtUtc,
            metadata.RelatedSourcePaths,
            content);
    }

    public IReadOnlyList<DemoWorkspaceFileSummary> ListDemoFiles(int maxResults = 200)
    {
        if (!Directory.Exists(DemoRoot))
        {
            return [];
        }

        int safeMaxResults = Math.Clamp(maxResults, 1, 1000);
        return Directory.EnumerateFiles(DemoRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsMetadataPath(path))
            .Select(path => CreateSummary(path))
            .OrderByDescending(summary => summary.UpdatedAtUtc)
            .ThenBy(summary => summary.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(safeMaxResults)
            .ToArray();
    }

    public string ResolveDemoPath(string relativePath)
    {
        string normalizedRelativePath = NormalizeRelativePath(relativePath);
        string fullRoot = Path.GetFullPath(DemoRoot);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelativePath));
        if (!IsPathInsideRoot(fullPath, fullRoot))
        {
            throw new InvalidOperationException("Demo path must stay under runtime/demos.");
        }

        return fullPath;
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Demo relative path is required.", nameof(relativePath));
        }

        string normalized = relativePath.Replace('\\', '/').Trim().TrimStart('/');
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException("Demo path must be relative.");
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException("Demo path must include a file name.");
        }

        foreach (string segment in segments)
        {
            if (segment.Equals(".", StringComparison.Ordinal) || segment.Equals("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Demo path cannot contain relative traversal segments.");
            }
        }

        if (segments.Any(segment => segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidOperationException("Demo path contains invalid file name characters.");
        }

        return string.Join('/', segments);
    }

    private DemoWorkspaceFileSummary CreateSummary(string fullPath)
    {
        string relativePath = Path.GetRelativePath(DemoRoot, fullPath).Replace('\\', '/');
        FileInfo fileInfo = new(fullPath);
        DemoWorkspaceFileMetadata metadata = ReadMetadata(fullPath, relativePath);
        return new DemoWorkspaceFileSummary(
            relativePath,
            fullPath,
            metadata.Purpose,
            metadata.Author,
            metadata.UpdatedAtUtc,
            fileInfo.Length,
            metadata.RelatedSourcePaths);
    }

    private DemoWorkspaceFileMetadata ReadMetadata(string fullPath, string relativePath)
    {
        string metadataPath = GetMetadataPath(fullPath);
        if (File.Exists(metadataPath))
        {
            string metadataJson = File.ReadAllText(metadataPath);
            DemoWorkspaceFileMetadata? metadata = JsonSerializer.Deserialize<DemoWorkspaceFileMetadata>(metadataJson, JsonOptions);
            if (metadata is not null)
            {
                return metadata;
            }
        }

        FileInfo fileInfo = new(fullPath);
        return new DemoWorkspaceFileMetadata(
            relativePath,
            fullPath,
            "Local demo or proposal file.",
            "unknown",
            new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
            []);
    }

    private static string GetMetadataPath(string fullPath)
    {
        return fullPath + ".metadata.json";
    }

    private static bool IsMetadataPath(string fullPath)
    {
        return fullPath.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathInsideRoot(string fullPath, string fullRoot)
    {
        string rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record DemoWorkspaceFileSummary(
    string RelativePath,
    string FullPath,
    string Purpose,
    string Author,
    DateTimeOffset UpdatedAtUtc,
    long Length,
    IReadOnlyList<string> RelatedSourcePaths);

public sealed record DemoWorkspaceFile(
    string RelativePath,
    string FullPath,
    string Purpose,
    string Author,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string> RelatedSourcePaths,
    string Content);

public sealed record DemoWorkspaceWriteResult(
    string RelativePath,
    string FullPath,
    string MetadataPath,
    long Length,
    DateTimeOffset UpdatedAtUtc,
    string Message);

public sealed record DemoWorkspaceDeleteResult(
    string RelativePath,
    string FullPath,
    string MetadataPath,
    bool FileDeleted,
    bool MetadataDeleted,
    string Message);

public sealed record DemoWorkspaceFileMetadata(
    string RelativePath,
    string FullPath,
    string Purpose,
    string Author,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string> RelatedSourcePaths)
{
    public string MetadataPath => FullPath + ".metadata.json";
}