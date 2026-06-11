@using BlazorDetectorSample.Models

<h1>@LegacyTitle</h1>
<p>@MixedModel.DisplayName</p>

@code {
    private string LegacyTitle { get; } = "Legacy mixed probe";

    private DetectorModel MixedModel { get; } = new() { DisplayName = "Mixed" };
}
