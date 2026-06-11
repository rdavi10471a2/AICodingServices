using BlazorDetectorSample.Models;

namespace BlazorDetectorSample.Components.Pages.Detector;

public partial class SplitProbe
{
    private string Title { get; } = "Split probe";

    private DetectorModel Current { get; } = new() { DisplayName = "Split" };
}
