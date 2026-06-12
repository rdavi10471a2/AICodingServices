using CodexUI.Models;

namespace CodexUI.Services;

public interface IMcpServerProcessService
{
    McpServerViewModel GetStatus();
}
