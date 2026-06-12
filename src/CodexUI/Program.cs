using CodexUI.Components;
using CodexUI.Data.Repositories;
using CodexUI.Services;

namespace CodexUI;

public static class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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
}
