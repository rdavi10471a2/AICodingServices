using CodexUI.Models;

namespace CodexUI.Services;

public interface IWatchedSolutionViewService
{
    WatchedSolutionViewModel GetView(string? selectedRelativePath, int? selectedLine);
}
