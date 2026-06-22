using AICodingServices.Core;
using AICodingServices.Workflow;

namespace AICodingServices.Workflow.Tests;

/// <summary>
/// Blazor test fixtures for workflow policy and tool-selection guidance tests.
/// Uses the same pattern as WorkflowFixture: creates temp directories with
/// minimal Blazor project structure for testing MCP workflow tools.
/// </summary>
public sealed class BlazorWorkflowTestFixtures
{
    /// <summary>
    /// Creates a minimal Blazor Server project with a counter component.
    /// Suitable for testing Razor markup edits, symbol edits, and scoped CSS.
    /// </summary>
    public static BlazorProjectFixture CreateBlazorServerFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AICodingServicesBlazorWorkflowTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string projectRoot = Path.Combine(repositoryRoot, "src", "BlazorApp");
        string projectPath = Path.Combine(projectRoot, "BlazorApp.csproj");
        string programPath = Path.Combine(projectRoot, Program.Path);
        string importsPath = Path.Combine(projectRoot, "_Imports.razor");
        string counterPath = Path.Combine(projectRoot, "Counter.razor");
        string counterCodePath = Path.Combine(projectRoot, "Counter.razor.cs");
        string counterCssPath = Path.Combine(projectRoot, "Counter.razor.css");
        string fetchDataPath = Path.Combine(projectRoot, "FetchData.razor");

        Directory.CreateDirectory(projectRoot);

        // Solution file
        string solutionPath = Path.Combine(repositoryRoot, "BlazorApp.slnx");
        File.WriteAllText(
            solutionPath,
            """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/BlazorApp/BlazorApp.csproj" />
              </Folder>
            </Solution>
            """);

        // Project file
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);

        // Program.cs
        File.WriteAllText(
            programPath,
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddRazorComponents();
            var app = builder.Build();
            app.MapRazorComponents<App>();
            app.Run();
            """);

        // _Imports.razor
        File.WriteAllText(
            importsPath,
            """
            @using System.Net.Http
            @using System.Text
            @using Microsoft.AspNetCore.Components.Forms
            @using Microsoft.AspNetCore.Components.Routing
            @using Microsoft.AspNetCore.Components.Web
            @using Microsoft.AspNetCore.Components.Web.Virtualization
            @using BlazorApp
            @using BlazorApp.Components
            """);

        // Counter.razor (markup)
        File.WriteAllText(
            counterPath,
            """
            @page "/counter"
            @rendermode InteractiveServer

            <PageTitle>Counter</PageTitle>

            <h1>Counter</h1>

            <p role="status">Current count: @currentCount</p>

            <button class="btn btn-primary" @onclick="IncrementCount">Click me</button>

            @code {
                private int currentCount = 0;

                private void IncrementCount()
                {
                    currentCount++;
                }
            }
            """);

        // Counter.razor.cs (code-behind)
        File.WriteAllText(
            counterCodePath,
            """
            namespace BlazorApp.Components;

            public partial class Counter : ComponentBase
            {
                private int currentCount = 0;

                private void IncrementCount()
                {
                    currentCount++;
                }
            }
            """);

        // Counter.razor.css (scoped)
        File.WriteAllText(
            counterCssPath,
            """
            h1 {
                color: var(--primary-color);
            }

            p[role="status"] {
                font-weight: bold;
                margin: 1rem 0;
            }
            """);

        // FetchData.razor (for testing multiple components)
        File.WriteAllText(
            fetchDataPath,
            """
            @page "/fetchdata"
            @inject HttpClient Http

            <PageTitle>Weather data</PageTitle>

            <h1>Weather data</h1>

            @if (forecasts == null)
            {
                <p><em>Loading...</em></p>
            }
            else
            {
                <table class="table">
                    <thead>
                        <tr>
                            <th>Date</th>
                            <th>Temp. (C)</th>
                            <th>Temp. (F)</th>
                            <th>Summary</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var forecast in forecasts)
                        {
                            <tr>
                                <td>@forecast.Date.ToShortDateString()</td>
                                <td>@forecast.TemperatureC</td>
                                <td>@forecast.TemperatureF</td>
                                <td>@forecast.Summary</td>
                            </tr>
                        }
                    </tbody>
                </table>
            }

            @code {
                private WeatherForecast[]? forecasts;

                protected override async Task OnInitializedAsync()
                {
                    forecasts = await Http.GetFromJsonAsync<WeatherForecast[]>("sample-data/weather.json");
                }

                public class WeatherForecast
                {
                    public DateOnly Date { get; set; }
                    public int TemperatureC { get; set; }
                    public string? Summary { get; set; }
                    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
                }
            }
            """);

        return new BlazorProjectFixture(
            MonitorSettings.Create(repositoryRoot, solutionPath, runtimeRoot),
            projectRoot,
            projectPath,
            programPath,
            importsPath,
            counterPath,
            counterCodePath,
            counterCssPath,
            fetchDataPath);
    }

    /// <summary>
    /// Creates a Blazor WebAssembly project with minimal structure.
    /// </summary>
    public static BlazorWasmProjectFixture CreateBlazorWasmFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AICodingServicesBlazorWasmTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string clientRoot = Path.Combine(repositoryRoot, "src", "BlazorWasmApp");
        string clientProjectPath = Path.Combine(clientRoot, "BlazorWasmApp.csproj");
        string indexPath = Path.Combine(clientRoot, "Pages", "Index.razor");
        string layoutPath = Path.Combine(clientRoot, "Shared", "MainLayout.razor");
        string layoutCssPath = Path.Combine(clientRoot, "Shared", "MainLayout.razor.css");
        string appPath = Path.Combine(clientRoot, "App.razor");

        Directory.CreateDirectory(Path.Combine(clientRoot, "Pages"));
        Directory.CreateDirectory(Path.Combine(clientRoot, "Shared"));

        string solutionPath = Path.Combine(repositoryRoot, "BlazorWasmApp.slnx");
        File.WriteAllText(
            solutionPath,
            """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/BlazorWasmApp/BlazorWasmApp.csproj" />
              </Folder>
            </Solution>
            """);

        File.WriteAllText(
            clientProjectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(
            appPath,
            """
            <Router AppAssembly="@typeof(App).Assembly">
                <Found Context="routeData">
                    <RouteView RouteData="@routeData" DefaultLayout="@typeof(Shared.MainLayout)" />
                </Found>
            </Router>
            """);

        File.WriteAllText(
            indexPath,
            """
            @page "/"

            <PageTitle>Home</PageTitle>

            <h1>Hello, Blazor!</h1>
            """);

        File.WriteAllText(
            layoutPath,
            """
            @inherits LayoutComponentBase

            <div class="main-layout">
                <nav class="sidebar">
                    <NavMenu />
                </nav>
                <main class="content">
                    @Body
                </main>
            </div>
            """);

        File.WriteAllText(
            layoutCssPath,
            """
            .main-layout {
                display: flex;
                height: 100vh;
            }

            .sidebar {
                width: 250px;
                background: #f5f5f5;
                padding: 1rem;
            }

            .content {
                flex: 1;
                padding: 2rem;
            }
            """);

        return new BlazorWasmProjectFixture(
            MonitorSettings.Create(repositoryRoot, solutionPath, runtimeRoot),
            clientRoot,
            clientProjectPath,
            indexPath,
            layoutPath,
            layoutCssPath);
    }
}

/// <summary>
/// Fixture for Blazor Server project tests.
/// </summary>
public sealed record BlazorProjectFixture(
    MonitorSettings Settings,
    string ProjectRoot,
    string ProjectPath,
    string ProgramPath,
    string ImportsPath,
    string CounterPath,
    string CounterCodePath,
    string CounterCssPath,
    string FetchDataPath);

/// <summary>
/// Fixture for Blazor WebAssembly project tests.
/// </summary>
public sealed record BlazorWasmProjectFixture(
    MonitorSettings Settings,
    string ClientRoot,
    string ClientProjectPath,
    string IndexPath,
    string LayoutPath,
    string LayoutCssPath);
