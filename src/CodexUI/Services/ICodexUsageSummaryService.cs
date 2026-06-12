using CodexUI.Models;

namespace CodexUI.Services;

public interface ICodexUsageSummaryService
{
    CodexUsageSummaryViewModel GetSummary();
}
