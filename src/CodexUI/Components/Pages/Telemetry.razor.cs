namespace CodexUI.Components.Pages;

using CodexUI.Models;
using CodexUI.Services;
using Microsoft.AspNetCore.Components;

public partial class Telemetry : ComponentBase
{
    private TelemetryViewModel model = TelemetryViewModel.Empty;

    [Inject]
    public ITelemetryViewService TelemetryViewService { get; set; } = default!;

    protected override void OnInitialized()
    {
        Refresh();
    }

    private void Refresh()
    {
        model = TelemetryViewService.GetTelemetry();
    }
}
