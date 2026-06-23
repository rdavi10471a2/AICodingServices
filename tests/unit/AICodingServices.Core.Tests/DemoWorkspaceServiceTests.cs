using AICodingServices.Core;

namespace AICodingServices.Core.Tests;

public sealed class DemoWorkspaceServiceTests
{
    [Fact]
    public void Write_read_list_and_delete_demo_file_under_runtime_demos()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            MonitorSettings settings = MonitorSettings.Create(
                tempRoot,
                Path.Combine(tempRoot, "Sample.sln"),
                Path.Combine(tempRoot, "runtime"));
            DemoWorkspaceService service = new(settings);

            DemoWorkspaceWriteResult write = service.WriteDemoFile(
                "proposals/sample.md",
                "# Sample\nBody",
                "Show a proposal",
                "test",
                ["src/App.cs"]);
            DemoWorkspaceFile read = service.ReadDemoFile("proposals/sample.md");
            IReadOnlyList<DemoWorkspaceFileSummary> list = service.ListDemoFiles();
            DemoWorkspaceDeleteResult delete = service.DeleteDemoFile("proposals/sample.md");

            Assert.Equal("proposals/sample.md", write.RelativePath);
            Assert.Equal("# Sample\nBody", read.Content);
            Assert.Equal("Show a proposal", read.Purpose);
            Assert.Equal("test", read.Author);
            Assert.Equal("src/App.cs", Assert.Single(read.RelatedSourcePaths));
            Assert.Equal("proposals/sample.md", Assert.Single(list).RelativePath);
            Assert.True(delete.FileDeleted);
            Assert.True(delete.MetadataDeleted);
            Assert.Empty(service.ListDemoFiles());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("proposals/../../outside.md")]
    [InlineData("C:/outside.md")]
    public void ResolveDemoPath_rejects_paths_outside_runtime_demos(string relativePath)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            MonitorSettings settings = MonitorSettings.Create(
                tempRoot,
                Path.Combine(tempRoot, "Sample.sln"),
                Path.Combine(tempRoot, "runtime"));
            DemoWorkspaceService service = new(settings);

            Assert.ThrowsAny<Exception>(() => service.ResolveDemoPath(relativePath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }
}