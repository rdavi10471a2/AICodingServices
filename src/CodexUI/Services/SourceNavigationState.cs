namespace CodexUI.Services;

public sealed class SourceNavigationState
{
    private readonly object syncRoot = new();

    private string? lastRelativePath;
    private int? lastLine;

    public string SourceHref
    {
        get
        {
            lock (syncRoot)
            {
                return BuildSourceHref(lastRelativePath, lastLine);
            }
        }
    }

    public string? LastRelativePath
    {
        get
        {
            lock (syncRoot)
            {
                return lastRelativePath;
            }
        }
    }

    public int? LastLine
    {
        get
        {
            lock (syncRoot)
            {
                return lastLine;
            }
        }
    }

    public void Remember(string? relativePath, int? line)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        lock (syncRoot)
        {
            lastRelativePath = relativePath;
            lastLine = Math.Max(line ?? 1, 1);
        }
    }

    public string? ResolvePath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            return requestedPath;
        }

        lock (syncRoot)
        {
            return lastRelativePath;
        }
    }

    public int? ResolveLine(int? requestedLine)
    {
        if (requestedLine is not null)
        {
            return requestedLine;
        }

        lock (syncRoot)
        {
            return lastLine;
        }
    }

    private static string BuildSourceHref(string? relativePath, int? line)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return "/watched-solution";
        }

        int safeLine = Math.Max(line ?? 1, 1);
        return $"/watched-solution?path={Uri.EscapeDataString(relativePath)}&line={safeLine}#line-{safeLine}";
    }
}
