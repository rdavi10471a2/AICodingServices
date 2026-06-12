using CodexUI.Components;
using CodexUI.Data.Repositories;
using CodexUI.Services;

namespace CodexUI;

public static class Program
{
    public static void Main(string[] args)
    {
        string contentRoot = ResolveContentRoot();
        WebApplicationOptions options = new()
        {
            Args = args,
            ContentRootPath = contentRoot,
            WebRootPath = Path.Combine(contentRoot, "wwwroot")
        };

        WebApplicationBuilder builder = WebApplication.CreateBuilder(options);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddSingleton<McpServerProcessService>();
        builder.Services.AddSingleton<IMcpServerProcessService>(
            services => services.GetRequiredService<McpServerProcessService>());
        builder.Services.AddHostedService(
            services => services.GetRequiredService<McpServerProcessService>());
        builder.Services.AddSingleton<ICodexUiMonitorSettingsProvider, CodexUiMonitorSettingsProvider>();
        builder.Services.AddSingleton<WatchedSolutionIndexRepository>();
        builder.Services.AddSingleton<WorkspaceRepository>();
        builder.Services.AddSingleton<IWorkspaceStatusService, WorkspaceStatusService>();
        builder.Services.AddSingleton<IDashboardViewService, PlaceholderDashboardViewService>();
        builder.Services.AddSingleton<IWatchedSolutionViewService, WatchedSolutionViewService>();

        WebApplication app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }

    private static string ResolveContentRoot()
    {
        string baseDirectory = AppContext.BaseDirectory;
        DirectoryInfo? current = new(baseDirectory);
        while (current is not null)
        {
            if (IsCodexUiProjectRoot(current.FullName))
            {
                return current.FullName;
            }

            string srcCandidate = Path.Combine(current.FullName, "src", "CodexUI");
            if (IsCodexUiProjectRoot(srcCandidate))
            {
                return srcCandidate;
            }

            current = current.Parent;
        }

        return baseDirectory;
    }

    private static bool IsCodexUiProjectRoot(string candidatePath)
    {
        return File.Exists(Path.Combine(candidatePath, "CodexUI.csproj"))
            && Directory.Exists(Path.Combine(candidatePath, "wwwroot"));
    }
}
