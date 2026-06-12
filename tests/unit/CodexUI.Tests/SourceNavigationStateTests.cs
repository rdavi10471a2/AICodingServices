using CodexUI.Services;

namespace CodexUI.Tests;

public sealed class SourceNavigationStateTests
{
    [Fact]
    public void SourceHref_uses_plain_source_route_before_selection()
    {
        SourceNavigationState state = new();

        Assert.Equal("/watched-solution", state.SourceHref);
        Assert.Null(state.LastRelativePath);
        Assert.Null(state.LastLine);
    }

    [Fact]
    public void Remember_tracks_last_source_path_and_line()
    {
        SourceNavigationState state = new();

        state.Remember(@"WEBSemanticModel\Model\ColumnBinding.cs", 13);

        Assert.Equal(@"WEBSemanticModel\Model\ColumnBinding.cs", state.LastRelativePath);
        Assert.Equal(13, state.LastLine);
        Assert.Equal(
            "/watched-solution?path=WEBSemanticModel%5CModel%5CColumnBinding.cs&line=13#line-13",
            state.SourceHref);
    }

    [Fact]
    public void Resolve_methods_prefer_requested_values_over_memory()
    {
        SourceNavigationState state = new();
        state.Remember(@"WEBSemanticModel\Model\ColumnBinding.cs", 13);

        Assert.Equal(@"WEBSemanticModel\Binding\ColumnBinder.cs", state.ResolvePath(@"WEBSemanticModel\Binding\ColumnBinder.cs"));
        Assert.Equal(42, state.ResolveLine(42));
    }

    [Fact]
    public void Resolve_methods_fall_back_to_memory_when_request_is_empty()
    {
        SourceNavigationState state = new();
        state.Remember(@"WEBSemanticModel\Model\ColumnBinding.cs", 13);

        Assert.Equal(@"WEBSemanticModel\Model\ColumnBinding.cs", state.ResolvePath(null));
        Assert.Equal(@"WEBSemanticModel\Model\ColumnBinding.cs", state.ResolvePath(string.Empty));
        Assert.Equal(13, state.ResolveLine(null));
    }
}
