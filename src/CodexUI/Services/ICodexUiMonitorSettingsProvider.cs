using AICodingServices.Core;

namespace CodexUI.Services;

public interface ICodexUiMonitorSettingsProvider
{
    MonitorSettings GetSettings();
    string GetSettingsPath();
}
