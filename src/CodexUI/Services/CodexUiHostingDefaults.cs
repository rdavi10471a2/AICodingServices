using Microsoft.AspNetCore.Hosting;

namespace CodexUI.Services;

public static class CodexUiHostingDefaults
{
    public const string DefaultHttpUrl = "http://localhost:5000";

    public static void ApplyDefaultUrl(WebApplicationBuilder builder, string[] args)
    {
        if (!ShouldUseFixedDefaultUrl(args, builder.Configuration[WebHostDefaults.ServerUrlsKey]))
        {
            return;
        }

        builder.WebHost.UseUrls(DefaultHttpUrl);
    }

    public static bool ShouldUseFixedDefaultUrl(string[] args, string? configuredUrls)
    {
        if (!string.IsNullOrWhiteSpace(configuredUrls))
        {
            return false;
        }

        foreach (string arg in args)
        {
            if (arg.Equals("--urls", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--url", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("--urls=", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("--url=", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
