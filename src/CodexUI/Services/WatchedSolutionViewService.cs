using AICodingServices.Core;
using AICodingServices.Data;
using CodexUI.Models;
using CodexUI.Data.Repositories;

namespace CodexUI.Services;

public sealed class WatchedSolutionViewService : IWatchedSolutionViewService
{
    private readonly ICodexUiMonitorSettingsProvider settingsProvider;
    private readonly IWorkspaceStatusService workspaceStatusService;

    private readonly DemoWorkspaceService demoWorkspaceService;
    private readonly WatchedSolutionIndexRepository watchedSolutionIndexRepository;

    public WatchedSolutionViewService(
        ICodexUiMonitorSettingsProvider settingsProvider,
        IWorkspaceStatusService workspaceStatusService,
        DemoWorkspaceService demoWorkspaceService,
        WatchedSolutionIndexRepository watchedSolutionIndexRepository)
    {
        this.settingsProvider = settingsProvider;
        this.workspaceStatusService = workspaceStatusService;
        this.demoWorkspaceService = demoWorkspaceService;
        this.watchedSolutionIndexRepository = watchedSolutionIndexRepository;
    }

    public WatchedSolutionViewModel GetView(string? selectedRelativePath, int? selectedLine, string? selectedDemoPath = null)
    {
        MonitorSettings settings = settingsProvider.GetSettings();
        WorkspaceStatusViewModel workspace = workspaceStatusService.EnsureWorkspace();
        IReadOnlyList<DemoWorkspaceFileSummary> demoSummaries = demoWorkspaceService.ListDemoFiles();
        SourceFileViewModel? selectedDemoFile = LoadSelectedDemoFile(selectedDemoPath, selectedLine);
        IReadOnlyList<SourceFileNodeViewModel> demoFiles = BuildDemoFileNodes(demoSummaries, selectedDemoFile);
        IReadOnlyList<SourceTreeNodeViewModel> demoTree = BuildDemoTree(demoSummaries, selectedDemoFile);
        string watchedRoot = Path.GetDirectoryName(settings.WatchedSolutionPath) ?? settings.WatchedProjectFolder;
        if (string.IsNullOrWhiteSpace(watchedRoot) || !Directory.Exists(watchedRoot))
        {
            return new WatchedSolutionViewModel(
                settings.WatchedSolutionPath,
                watchedRoot,
                "Watched solution folder was not found.",
                [],
                [],
                demoFiles,
                demoTree,
                workspace,
                selectedDemoFile,
                selectedDemoFile is not null);
        }

        WatchedSolutionIndexSnapshot snapshot = watchedSolutionIndexRepository.LoadSnapshot(settings);
        IReadOnlyList<IndexedProjectRow> projects = snapshot.Projects;
        IReadOnlyList<IndexedDocumentRow> documents = snapshot.Documents;
        IReadOnlyList<IndexedSymbolRow> symbols = snapshot.Symbols;
        if (projects.Count == 0 || documents.Count == 0)
        {
            return new WatchedSolutionViewModel(
                settings.WatchedSolutionPath,
                watchedRoot,
                "Index is empty. Rebuild the index to load the solution tree.",
                [],
                [],
                demoFiles,
                demoTree,
                workspace,
                selectedDemoFile,
                selectedDemoFile is not null);
        }

        SourceFileViewModel? selectedFile = selectedDemoFile;
        IReadOnlyList<SourceFileNodeViewModel> files = BuildFileNodes(watchedRoot, documents, symbols, selectedDemoFile is null ? selectedRelativePath : null, selectedLine, out SourceFileViewModel? watchedSelectedFile);
        if (selectedFile is null)
        {
            selectedFile = watchedSelectedFile;
        }

        IReadOnlyList<SourceTreeNodeViewModel> tree = BuildTree(projects, documents, symbols, watchedRoot, selectedFile);

        return new WatchedSolutionViewModel(
            settings.WatchedSolutionPath,
            watchedRoot,
            selectedFile is null ? "Select a source file." : "Read-only source view.",
            files,
            tree,
            demoFiles,
            demoTree,
            workspace,
            selectedFile,
            selectedDemoFile is not null);
    }

    private static IReadOnlyList<SourceFileNodeViewModel> BuildFileNodes(
        string watchedRoot,
        IReadOnlyList<IndexedDocumentRow> documents,
        IReadOnlyList<IndexedSymbolRow> symbols,
        string? selectedRelativePath,
        int? selectedLine,
        out SourceFileViewModel? selectedFile)
    {
        List<string> relativePaths = documents
            .Select(document => Path.GetRelativePath(watchedRoot, document.FilePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        string? selectedPath = SelectRelativePath(relativePaths, selectedRelativePath);
        selectedFile = selectedPath is null
            ? null
            : LoadFile(watchedRoot, selectedPath, selectedLine);
        SourceFileViewModel? selected = selectedFile;

        return relativePaths
            .Select(path => new SourceFileNodeViewModel(
                path,
                Path.GetFileName(path),
                Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
                string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase),
                BuildOutlineFromIndex(symbols, watchedRoot, path, selected)))
            .ToArray();
    }

    private SourceFileViewModel? LoadSelectedDemoFile(string? selectedDemoPath, int? selectedLine)
    {
        if (string.IsNullOrWhiteSpace(selectedDemoPath))
        {
            return null;
        }

        DemoWorkspaceFile demoFile = demoWorkspaceService.ReadDemoFile(selectedDemoPath);
        string[] lines = demoFile.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        int safeSelectedLine = Math.Clamp(selectedLine ?? 1, 1, Math.Max(lines.Length, 1));
        return new SourceFileViewModel(
            demoFile.RelativePath,
            demoFile.FullPath,
            GetLanguage(Path.GetExtension(demoFile.FullPath)),
            safeSelectedLine,
            lines);
    }

    private static IReadOnlyList<SourceFileNodeViewModel> BuildDemoFileNodes(
    IReadOnlyList<DemoWorkspaceFileSummary> demos,
    SourceFileViewModel? selectedDemoFile)
    {
        return demos
            .OrderBy(demo => demo.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(demo => new SourceFileNodeViewModel(
                demo.RelativePath,
                Path.GetFileName(demo.RelativePath),
                Path.GetExtension(demo.RelativePath).TrimStart('.').ToUpperInvariant(),
                selectedDemoFile is not null && PathEquals(selectedDemoFile.FullPath, demo.FullPath),
                []))
            .ToArray();
    }

    private static IReadOnlyList<SourceTreeNodeViewModel> BuildDemoTree(
    IReadOnlyList<DemoWorkspaceFileSummary> demos,
    SourceFileViewModel? selectedDemoFile)
    {
        MutableTreeNode root = new(string.Empty, true);
        foreach (DemoWorkspaceFileSummary demo in demos.OrderBy(demo => demo.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            string[] segments = NormalizePath(demo.RelativePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            MutableTreeNode current = root;
            for (int index = 0; index < segments.Length; index++)
            {
                bool isFile = index == segments.Length - 1;
                current = current.GetOrAdd(segments[index], !isFile);
                current.LinkKind = SourceTreeLinkKind.Demo;
                if (!isFile)
                {
                    continue;
                }

                current.RelativePath = demo.RelativePath;
                current.Extension = Path.GetExtension(demo.RelativePath).TrimStart('.').ToUpperInvariant();
                current.IsSelected = selectedDemoFile is not null && PathEquals(selectedDemoFile.FullPath, demo.FullPath);
                current.Kind = "file";
                current.Line = 1;
            }
        }

        return root.Children
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ConvertTreeNode)
            .ToArray();
    }

    private static IReadOnlyList<SourceTreeNodeViewModel> BuildTree(
        IReadOnlyList<IndexedProjectRow> projects,
        IReadOnlyList<IndexedDocumentRow> documents,
        IReadOnlyList<IndexedSymbolRow> symbols,
        string watchedRoot,
        SourceFileViewModel? selectedFile)
    {
        MutableTreeNode root = new(string.Empty, true);
        foreach (IndexedProjectRow project in projects.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
        {
            MutableTreeNode projectNode = root.GetOrAdd(project.Name, true);
            projectNode.Kind = "project";
            foreach (IndexedDocumentRow document in documents
                .Where(document => PathEquals(document.ProjectPath, project.ProjectPath))
                .OrderBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                AddDocumentNode(projectNode, project.ProjectPath, watchedRoot, document, symbols, selectedFile);
            }
        }

        return root.Children
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ConvertTreeNode)
            .ToArray();
    }

    private static void AddDocumentNode(
        MutableTreeNode projectNode,
        string projectPath,
        string watchedRoot,
        IndexedDocumentRow document,
        IReadOnlyList<IndexedSymbolRow> symbols,
        SourceFileViewModel? selectedFile)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath) ?? watchedRoot;
        string relativeToProject = Path.GetRelativePath(projectDirectory, document.FilePath);
        string[] segments = NormalizePath(relativeToProject).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(IsHiddenBuildFolder))
        {
            return;
        }

        MutableTreeNode current = projectNode;
        for (int index = 0; index < segments.Length; index++)
        {
            bool isFile = index == segments.Length - 1;
            current = current.GetOrAdd(segments[index], !isFile);
            if (!isFile)
            {
                continue;
            }

            string relativeToRoot = Path.GetRelativePath(watchedRoot, document.FilePath);
            bool selected = selectedFile is not null
                && PathEquals(selectedFile.FullPath, document.FilePath);
            current.RelativePath = relativeToRoot;
            current.Extension = Path.GetExtension(document.FilePath).TrimStart('.').ToUpperInvariant();
            current.IsSelected = selected;
            current.Kind = "file";
            current.Line = 1;
            current.Outline = BuildOutlineFromIndex(symbols, watchedRoot, relativeToRoot, selectedFile);
            AddSymbolNodes(current, document, symbols, relativeToRoot, selectedFile);
        }

        if (selectedFile is not null && PathEquals(selectedFile.FullPath, document.FilePath))
        {
            current = projectNode;
            for (int index = 0; index < segments.Length; index++)
            {
                bool isFile = index == segments.Length - 1;
                current = current.GetOrAdd(segments[index], !isFile);
                current.IsSelectedAncestor = true;
            }

            projectNode.IsSelectedAncestor = true;
        }
    }

    private static SourceTreeNodeViewModel ConvertTreeNode(MutableTreeNode node)
    {
        IReadOnlyList<SourceTreeNodeViewModel> children = node.Children
            .OrderBy(child => GetTreeNodeRank(child))
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ConvertTreeNode)
            .ToArray();

        return new SourceTreeNodeViewModel(
            node.Name,
            node.RelativePath,
            node.Extension,
            node.IsFolder,
            node.IsSelected,
            node.IsSelectedAncestor,
            node.Line,
            node.Kind,
            children,
            node.Outline,
            node.LinkKind);
    }

    private static int GetTreeNodeRank(MutableTreeNode node)
    {
        if (node.Kind == "project")
        {
            return 0;
        }

        if (node.Kind == "folder")
        {
            return 1;
        }

        if (node.Kind == "file")
        {
            return 2;
        }

        if (node.Kind == "type")
        {
            return 3;
        }

        if (node.Kind == "group")
        {
            return 4 + GetSymbolGroupRank(node.Name);
        }

        return 20;
    }

    private static void AddSymbolNodes(
        MutableTreeNode documentNode,
        IndexedDocumentRow document,
        IReadOnlyList<IndexedSymbolRow> symbols,
        string relativeToRoot,
        SourceFileViewModel? selectedFile)
    {
        Dictionary<string, MutableTreeNode> typeNodes = new(StringComparer.Ordinal);
        foreach (IndexedSymbolRow symbol in symbols
            .Where(symbol => PathEquals(symbol.FilePath, document.FilePath))
            .OrderBy(symbol => symbol.StartLine)
            .ThenBy(symbol => symbol.Name, StringComparer.Ordinal))
        {
            if (IsTypeLikeSymbol(symbol))
            {
                MutableTreeNode typeNode = documentNode.GetOrAdd(FormatSymbolNode(symbol), true);
                typeNode.RelativePath = relativeToRoot;
                typeNode.Line = Math.Max(symbol.StartLine, 1);
                typeNode.Kind = "type";
                typeNode.IsSelected = IsSelectedSymbol(selectedFile, document.FilePath, typeNode.Line);
                typeNodes[symbol.Signature] = typeNode;
                continue;
            }

            MutableTreeNode parent = documentNode;
            if (!string.IsNullOrWhiteSpace(symbol.ContainingType)
                && typeNodes.TryGetValue(symbol.ContainingType, out MutableTreeNode? typeParent))
            {
                parent = typeParent;
            }

            MutableTreeNode group = GetOrAddSymbolGroupNode(parent, symbol);
            MutableTreeNode symbolNode = group.GetOrAdd(FormatSymbolNode(symbol), false);
            symbolNode.RelativePath = relativeToRoot;
            symbolNode.Extension = symbol.Kind;
            symbolNode.Line = Math.Max(symbol.StartLine, 1);
            symbolNode.Kind = "symbol";
            symbolNode.IsSelected = IsSelectedSymbol(selectedFile, document.FilePath, symbolNode.Line);
        }
    }

    private static bool IsSelectedSymbol(SourceFileViewModel? selectedFile, string filePath, int line)
    {
        return selectedFile is not null
            && selectedFile.SelectedLine == line
            && PathEquals(selectedFile.FullPath, filePath);
    }

    private static MutableTreeNode GetOrAddSymbolGroupNode(MutableTreeNode parent, IndexedSymbolRow symbol)
    {
        MutableTreeNode group = parent.GetOrAdd(FormatSymbolGroupName(symbol), true);
        group.Kind = "group";
        return group;
    }

    private static string FormatSymbolNode(IndexedSymbolRow symbol)
    {
        string signature = FormatLocalSignature(symbol);
        return $"{signature} [{symbol.StartLine}-{symbol.EndLine}]";
    }

    private static string FormatLocalSignature(IndexedSymbolRow symbol)
    {
        if (IsTypeLikeSymbol(symbol))
        {
            return symbol.Name;
        }

        string signature = string.IsNullOrWhiteSpace(symbol.Signature)
            ? symbol.Name
            : symbol.Signature;
        string localSignature = RemoveContainingTypePrefix(signature, symbol);
        return SimplifySignatureTypes(localSignature);
    }

    private static string RemoveContainingTypePrefix(string signature, IndexedSymbolRow symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol.ContainingType))
        {
            string containingPrefix = symbol.ContainingType + ".";
            if (signature.StartsWith(containingPrefix, StringComparison.Ordinal))
            {
                return signature[containingPrefix.Length..];
            }
        }

        if (!string.IsNullOrWhiteSpace(symbol.Namespace))
        {
            string namespacePrefix = symbol.Namespace + ".";
            if (signature.StartsWith(namespacePrefix, StringComparison.Ordinal))
            {
                return signature[namespacePrefix.Length..];
            }
        }

        return signature;
    }

    private static string SimplifySignatureTypes(string signature)
    {
        int parameterStart = signature.IndexOf('(', StringComparison.Ordinal);
        int parameterEnd = signature.LastIndexOf(')');
        if (parameterStart < 0 || parameterEnd <= parameterStart)
        {
            return GetUnqualifiedTypeName(signature);
        }

        string name = signature[..parameterStart];
        string parameters = signature[(parameterStart + 1)..parameterEnd];
        string suffix = signature[(parameterEnd + 1)..];
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return $"{GetUnqualifiedTypeName(name)}(){suffix}";
        }

        string[] parts = parameters.Split(',');
        for (int index = 0; index < parts.Length; index++)
        {
            parts[index] = SimplifyParameter(parts[index].Trim());
        }

        return $"{GetUnqualifiedTypeName(name)}({string.Join(", ", parts)}){suffix}";
    }

    private static string SimplifyParameter(string parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return parameter;
        }

        string[] tokens = parameter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < tokens.Length; index++)
        {
            tokens[index] = SimplifyTypeExpression(tokens[index]);
        }

        return string.Join(" ", tokens);
    }

    private static string SimplifyTypeExpression(string text)
    {
        return text
            .Replace("Microsoft.Data.Sqlite.", string.Empty, StringComparison.Ordinal)
            .Replace("System.Collections.Generic.", string.Empty, StringComparison.Ordinal)
            .Replace("System.Threading.Tasks.", string.Empty, StringComparison.Ordinal)
            .Replace("System.Threading.", string.Empty, StringComparison.Ordinal)
            .Replace("System.", string.Empty, StringComparison.Ordinal);
    }

    private static string GetUnqualifiedTypeName(string name)
    {
        int genericIndex = name.IndexOf('<', StringComparison.Ordinal);
        string rootName = genericIndex >= 0
            ? name[..genericIndex]
            : name;
        int lastDot = rootName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < rootName.Length - 1)
        {
            rootName = rootName[(lastDot + 1)..];
        }

        return genericIndex >= 0
            ? rootName + name[genericIndex..]
            : rootName;
    }

    private static string FormatSymbolGroupName(IndexedSymbolRow symbol)
    {
        string access = FormatAccessibility(symbol.Accessibility);
        string category = FormatSymbolCategory(symbol);
        return $"{access} {category}";
    }

    private static string FormatSymbolCategory(IndexedSymbolRow symbol)
    {
        if (IsTypeLikeSymbol(symbol))
        {
            return "types";
        }

        if (IsConstructorSymbol(symbol))
        {
            return "constructors";
        }

        if (symbol.Kind.Equals("Method", StringComparison.OrdinalIgnoreCase))
        {
            return "methods";
        }

        return "members";
    }

    private static string FormatAccessibility(string accessibility)
    {
        return string.IsNullOrWhiteSpace(accessibility)
            ? "access unknown"
            : accessibility.ToLowerInvariant();
    }

    private static int GetSymbolGroupRank(string groupName)
    {
        if (groupName.Contains("constructors", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (groupName.Contains("types", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (groupName.Contains("methods", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (groupName.Contains("members", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 4;
    }

    private static bool IsTypeLikeSymbol(IndexedSymbolRow symbol)
    {
        return symbol.Kind.Equals("NamedType", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Class", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Struct", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Interface", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Enum", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Record", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConstructorSymbol(IndexedSymbolRow symbol)
    {
        return symbol.Kind.Equals("Method", StringComparison.OrdinalIgnoreCase)
            && (symbol.MethodKind.Equals("Constructor", StringComparison.OrdinalIgnoreCase)
                || symbol.MethodKind.Equals("StaticConstructor", StringComparison.OrdinalIgnoreCase)
                || symbol.Name.Equals(".ctor", StringComparison.Ordinal)
                || symbol.Name.Equals(".cctor", StringComparison.Ordinal));
    }

    private static bool IsHiddenBuildFolder(string segment)
    {
        return segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("CSSourceBackups", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SelectRelativePath(IReadOnlyList<string> relativePaths, string? selectedRelativePath)
    {
        if (!string.IsNullOrWhiteSpace(selectedRelativePath))
        {
            string? match = relativePaths.FirstOrDefault(path =>
                string.Equals(path, selectedRelativePath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizePath(path), NormalizePath(selectedRelativePath), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return relativePaths.FirstOrDefault();
    }

    private static SourceFileViewModel LoadFile(string watchedRoot, string relativePath, int? selectedLine)
    {
        string fullPath = Path.GetFullPath(Path.Combine(watchedRoot, relativePath));
        string fullRoot = Path.GetFullPath(watchedRoot);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Selected file is outside the watched solution root.");
        }

        string[] lines = File.ReadAllLines(fullPath);
        int safeSelectedLine = Math.Clamp(selectedLine ?? 1, 1, Math.Max(lines.Length, 1));
        return new SourceFileViewModel(
            relativePath,
            fullPath,
            GetLanguage(Path.GetExtension(fullPath)),
            safeSelectedLine,
            lines);
    }

    private static IReadOnlyList<SourceOutlineNodeViewModel> BuildOutlineFromIndex(
        IReadOnlyList<IndexedSymbolRow> symbols,
        string watchedRoot,
        string relativePath,
        SourceFileViewModel? selectedFile)
    {
        string fullPath = Path.GetFullPath(Path.Combine(watchedRoot, relativePath));
        bool fileSelected = selectedFile is not null && PathEquals(selectedFile.FullPath, fullPath);
        return symbols
            .Where(symbol => PathEquals(symbol.FilePath, fullPath))
            .OrderBy(symbol => symbol.StartLine)
            .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
            .Take(80)
            .Select(symbol => new SourceOutlineNodeViewModel(
                symbol.Name,
                symbol.Kind,
                symbol.StartLine,
                fileSelected && selectedFile?.SelectedLine == symbol.StartLine))
            .ToArray();
    }

    private static string GetLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".razor" => "Razor",
            ".cshtml" => "Razor",
            ".json" => "JSON",
            ".xml" => "XML",
            ".xaml" => "XAML",
            ".md" => "Markdown",
            ".css" => "CSS",
            ".html" => "HTML",
            ".js" => "JavaScript",
            ".ts" => "TypeScript",
            ".csproj" => "MSBuild",
            ".sln" => "Solution",
            ".slnx" => "Solution",
            _ => "Text"
        };
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static bool PathEquals(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MutableTreeNode
    {
        private readonly Dictionary<string, MutableTreeNode> children = new(StringComparer.OrdinalIgnoreCase);

        public MutableTreeNode(string name, bool isFolder)
        {
            Name = name;
            IsFolder = isFolder;
            Kind = isFolder ? "folder" : "file";
            Line = 1;
        }

        public string Name { get; }

        public bool IsFolder { get; }

        public string? RelativePath { get; set; }

        public string Extension { get; set; } = string.Empty;

        public bool IsSelected { get; set; }

        public bool IsSelectedAncestor { get; set; }

        public int Line { get; set; }

        public string Kind { get; set; }

        public string LinkKind { get; set; } = SourceTreeLinkKind.Watched;

        public IReadOnlyList<SourceOutlineNodeViewModel> Outline { get; set; } = [];

        public IEnumerable<MutableTreeNode> Children => children.Values;

        public MutableTreeNode GetOrAdd(string name, bool isFolder)
        {
            if (children.TryGetValue(name, out MutableTreeNode? child))
            {
                return child;
            }

            child = new MutableTreeNode(name, isFolder);
            children.Add(name, child);
            return child;
        }
    }
}
