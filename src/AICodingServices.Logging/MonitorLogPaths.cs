using AICodingServices.Core;
using System.Globalization;

namespace AICodingServices.Logging;

public static class MonitorLogPaths
{
    public static string GetDefaultLogPath(MonitorSettings settings)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture);
        string processId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        string fileName = $"aimonitor-{timestamp}-{processId}.ndjson";
        return Path.Combine(settings.RuntimeRoot, "logs", fileName);
    }
}