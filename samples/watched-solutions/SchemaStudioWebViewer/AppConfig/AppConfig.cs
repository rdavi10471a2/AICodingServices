using Microsoft.Extensions.Configuration;
using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.Configuration
{
    [FileVersion("1.3")]
    [AIChange("1.2", "2026-04-22 11:34 AM CDT guarded Auth.InDebug with Debugger.IsAttached so debug autofill is disabled outside an active debugger session.", AICommandStatus.Pending)]
    [AIChange("1.1", "2026-04-22 11:30 AM CDT added Auth.InDebug so the temporary login form can autofill debug credentials when configured.", AICommandStatus.Pending)]
    [AIFileContext("AppConfig/AppConfig.cs", "Loads strongly typed application settings for database, MCP, kiosk, and simple-auth policy switches.")]
    [AIChange("1.0", "2026-04-22 11:14 AM CDT added kiosk and simple-auth configuration switches with top-level usings and explicit configuration reads.", AICommandStatus.Pending)]
    // 2026-04-22 11:30 AM CDT AI v1.1 config marker: Auth.InDebug controls local autofill for the temporary simple login form.
    // 2026-04-22 11:14 AM CDT AI v1.0 config marker: kiosk and simple-auth switches keep unsettled login/display policy configurable.
    public class AppConfig
    {
        public static AppConfig Current { get; private set; } = new AppConfig();

        public DBConnectionString ConnectionStrings { get; set; } = new DBConnectionString();

        public McpConfig Mcp { get; set; } = new McpConfig();

        public KioskConfig Kiosk { get; set; } = new KioskConfig();

        public SimpleAuthConfig Auth { get; set; } = new SimpleAuthConfig();

        public static void Initialize(IConfiguration configuration)
        {
            Current = new AppConfig
            {
                ConnectionStrings = new DBConnectionString
                {
                    DefaultConnection = ReadString(configuration, "ConnectionStrings:DefaultConnection", string.Empty)
                },
                Mcp = new McpConfig
                {
                    Enabled = ReadBool(configuration, "Mcp:Enabled", false),
                    BaseRoute = ReadString(configuration, "Mcp:BaseRoute", "/mcp"),
                    UseSse = ReadBool(configuration, "Mcp:UseSse", true)
                },
                Kiosk = new KioskConfig
                {
                    Enabled = ReadBool(configuration, "Kiosk:Enabled", true),
                    Route = ReadString(configuration, "Kiosk:Route", "/kiosk"),
                    HideNavigationChrome = ReadBool(configuration, "Kiosk:HideNavigationChrome", true),
                    RequireLogin = ReadBool(configuration, "Kiosk:RequireLogin", false),
                    ShowLoginLink = ReadBool(configuration, "Kiosk:ShowLoginLink", false)
                },
                Auth = new SimpleAuthConfig
                {
                    Enabled = ReadBool(configuration, "Auth:Enabled", false),
                    InDebug = ReadBool(configuration, "Auth:InDebug", false),
                    RequireLoginForHome = ReadBool(configuration, "Auth:RequireLoginForHome", false),
                    RequireLoginForAdmin = ReadBool(configuration, "Auth:RequireLoginForAdmin", true),
                    LoginPath = ReadString(configuration, "Auth:LoginPath", "/login")
                }
            };
        }

        private static string ReadString(IConfiguration configuration, string key, string fallback)
        {
            var value = configuration[key];
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static bool ReadBool(IConfiguration configuration, string key, bool fallback)
        {
            return bool.TryParse(configuration[key], out var value) ? value : fallback;
        }
    }

    public class DBConnectionString
    {
        public string DefaultConnection { get; set; } = "";
    }

    public class McpConfig
    {
        public bool Enabled { get; set; } = false;

        public string BaseRoute { get; set; } = "/mcp";

        public bool UseSse { get; set; } = true;

        public string EffectiveRoute
        {
            get
            {
                if (UseSse)
                {
                    return $"{BaseRoute.TrimEnd('/')}/sse";
                }

                return BaseRoute;
            }
        }
    }

    public class KioskConfig
    {
        public bool Enabled { get; set; } = true;

        public string Route { get; set; } = "/kiosk";

        public bool HideNavigationChrome { get; set; } = true;

        public bool RequireLogin { get; set; } = false;

        public bool ShowLoginLink { get; set; } = false;
    }

    public class SimpleAuthConfig
    {
        public bool Enabled { get; set; } = false;

        public bool InDebug { get; set; } = false;

        public bool RequireLoginForHome { get; set; } = false;

        public bool RequireLoginForAdmin { get; set; } = true;

        public string LoginPath { get; set; } = "/login";
    }
}
