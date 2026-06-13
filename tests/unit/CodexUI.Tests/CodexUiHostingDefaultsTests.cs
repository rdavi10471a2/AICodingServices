using CodexUI.Services;

namespace CodexUI.Tests;

public sealed class CodexUiHostingDefaultsTests
{
    [Fact]
    public void ShouldUseFixedDefaultUrl_returns_true_when_no_override_is_present()
    {
        bool result = CodexUiHostingDefaults.ShouldUseFixedDefaultUrl([], configuredUrls: null);

        Assert.True(result);
    }

    [Theory]
    [InlineData("http://localhost:6100")]
    [InlineData("http://localhost:5000;https://localhost:5001")]
    public void ShouldUseFixedDefaultUrl_returns_false_when_configuration_already_supplies_urls(string configuredUrls)
    {
        bool result = CodexUiHostingDefaults.ShouldUseFixedDefaultUrl([], configuredUrls);

        Assert.False(result);
    }

    [Theory]
    [InlineData("--urls")]
    [InlineData("--url")]
    [InlineData("--urls=http://localhost:6100")]
    [InlineData("--url=http://localhost:6100")]
    public void ShouldUseFixedDefaultUrl_returns_false_when_args_override_urls(string arg)
    {
        bool result = CodexUiHostingDefaults.ShouldUseFixedDefaultUrl([arg], configuredUrls: null);

        Assert.False(result);
    }
}
