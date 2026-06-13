using AICodingServices.Workflow;
using System.Diagnostics;

namespace AICodingServices.Runtime;

public enum StagedReviewLaunchSurface
{
    WinMerge,
    Browser
}

public sealed class BrowserStagedReviewLauncher
{
    private static readonly object ActiveReviewWindowsLock = new();
    private static readonly Dictionary<string, BrowserReviewWindowState> ActiveReviewWindows = new(StringComparer.Ordinal);

    public DiffLaunchResult Launch(string reviewBaseUrl, StagedEditRecord record, string? browserPath = null)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        string reviewUrl = BuildReviewUrl(reviewBaseUrl, record);
        string cycleKey = GetCycleKey(record);
        BrowserReviewWindowState? activeWindow = GetActiveWindow(cycleKey);
        if (activeWindow is not null)
        {
            return new DiffLaunchResult
            {
                Launched = true,
                Tool = "Browser",
                ToolPath = activeWindow.ReviewUrl,
                ProcessId = activeWindow.ProcessId,
                Message = $"Reusing existing browser review window for {reviewUrl}."
            };
        }

        if (IsBrowserProcessDisabled())
        {
            RememberActiveWindow(cycleKey, reviewUrl, 0);
            return new DiffLaunchResult
            {
                Launched = true,
                Tool = "Browser",
                ToolPath = reviewUrl,
                ProcessId = 0,
                Message = $"Browser process launch is disabled. Review is ready at {reviewUrl}."
            };
        }

        ProcessStartInfo startInfo = CreateReviewStartInfo(reviewUrl, browserPath);
        Process? process = Process.Start(startInfo);
        int processId = process?.Id ?? 0;
        RememberActiveWindow(cycleKey, reviewUrl, processId);

        return new DiffLaunchResult
        {
            Launched = true,
            Tool = "Browser",
            ToolPath = startInfo.FileName,
            ProcessId = processId,
            Message = processId == 0
                ? $"Opened browser review at {reviewUrl}. Keep this review window for the current edit cycle."
                : $"Opened browser review at {reviewUrl} in a dedicated browser window. Keep this window for the current edit cycle."
        };
    }

    public DiffLaunchResult Launch(string reviewBaseUrl, string stagedRecordId, string? browserPath = null)
    {
        StagedEditRecord record = new()
        {
            StagedRecordId = stagedRecordId
        };
        return Launch(reviewBaseUrl, record, browserPath);
    }

    public static DiffLaunchResult CreateReuseResult(string reviewBaseUrl, StagedEditRecord record)
    {
        string reviewUrl = BuildReviewUrl(reviewBaseUrl, record);
        return new DiffLaunchResult
        {
            Launched = true,
            Tool = "Browser",
            ToolPath = reviewUrl,
            ProcessId = 0,
            Message = $"Reusing existing browser review session at {reviewUrl}."
        };
    }

    public static ProcessStartInfo CreateReviewStartInfo(string reviewUrl, string? browserPath = null)
    {
        if (string.IsNullOrWhiteSpace(reviewUrl))
        {
            throw new ArgumentException("Review URL is required.", nameof(reviewUrl));
        }

        string? resolvedBrowserPath = ResolveBrowserPath(browserPath);
        if (string.IsNullOrWhiteSpace(resolvedBrowserPath))
        {
            return new ProcessStartInfo
            {
                FileName = reviewUrl,
                UseShellExecute = true
            };
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = resolvedBrowserPath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--new-window");
        startInfo.ArgumentList.Add(reviewUrl);
        return startInfo;
    }

    public static string BuildReviewUrl(string reviewBaseUrl, StagedEditRecord record)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        if (!string.IsNullOrWhiteSpace(record.SessionId))
        {
            return BuildSessionReviewUrl(reviewBaseUrl, record.SessionId);
        }

        return BuildReviewUrl(reviewBaseUrl, record.StagedRecordId);
    }

    public static string BuildReviewUrl(string reviewBaseUrl, string stagedRecordId)
    {
        if (string.IsNullOrWhiteSpace(reviewBaseUrl))
        {
            throw new ArgumentException("Review base URL is required.", nameof(reviewBaseUrl));
        }

        if (string.IsNullOrWhiteSpace(stagedRecordId))
        {
            throw new ArgumentException("Staged record id is required.", nameof(stagedRecordId));
        }

        string normalizedBaseUrl = reviewBaseUrl.Trim().TrimEnd('/');
        string encodedRecordId = Uri.EscapeDataString(stagedRecordId.Trim());
        return $"{normalizedBaseUrl}/review/staged/{encodedRecordId}";
    }

    public static string BuildSessionReviewUrl(string reviewBaseUrl, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(reviewBaseUrl))
        {
            throw new ArgumentException("Review base URL is required.", nameof(reviewBaseUrl));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        string normalizedBaseUrl = reviewBaseUrl.Trim().TrimEnd('/');
        string encodedSessionId = Uri.EscapeDataString(sessionId.Trim());
        return $"{normalizedBaseUrl}/review/session/{encodedSessionId}";
    }

    private static string GetCycleKey(StagedEditRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.SessionId))
        {
            return "session:" + record.SessionId.Trim();
        }

        return "staged:" + record.StagedRecordId.Trim();
    }

    private static BrowserReviewWindowState? GetActiveWindow(string cycleKey)
    {
        lock (ActiveReviewWindowsLock)
        {
            if (!ActiveReviewWindows.TryGetValue(cycleKey, out BrowserReviewWindowState? state))
            {
                return null;
            }

            if (state.ProcessId == 0 || IsProcessRunning(state.ProcessId))
            {
                return state;
            }

            ActiveReviewWindows.Remove(cycleKey);
            return null;
        }
    }

    private static void RememberActiveWindow(string cycleKey, string reviewUrl, int processId)
    {
        lock (ActiveReviewWindowsLock)
        {
            ActiveReviewWindows[cycleKey] = new BrowserReviewWindowState(reviewUrl, processId);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsBrowserProcessDisabled()
    {
        string? value = Environment.GetEnvironmentVariable("AIMONITOR_DISABLE_BROWSER_PROCESS");
        return value is not null
            && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveBrowserPath(string? browserPath)
    {
        if (!string.IsNullOrWhiteSpace(browserPath) && File.Exists(browserPath))
        {
            return browserPath;
        }

        foreach (string candidatePath in EnumerateBrowserCandidatePaths())
        {
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateBrowserCandidatePaths()
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        ];

        foreach (string root in roots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            yield return Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
            yield return Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
        }
    }

    private sealed record BrowserReviewWindowState(string ReviewUrl, int ProcessId);
}