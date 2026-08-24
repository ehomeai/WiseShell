using WiseShell.Transport.SshNet.Services;

namespace WiseShell.Tests;

public sealed class LocalUploadExclusionsTests
{
    [Theory]
    [InlineData(".svn")]
    [InlineData(".git")]
    [InlineData(".vs")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    public void ShouldExcludeDirectory_SkipsSourceControlAndGeneratedDirectories(string directoryName)
    {
        var directoryPath = Path.Combine(@"D:\work\project", directoryName);

        Assert.True(LocalUploadExclusions.ShouldExcludeDirectory(directoryPath));
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("src")]
    [InlineData("assets")]
    public void ShouldExcludeDirectory_KeepsOrdinaryProjectDirectories(string directoryName)
    {
        var directoryPath = Path.Combine(@"D:\work\project", directoryName);

        Assert.False(LocalUploadExclusions.ShouldExcludeDirectory(directoryPath));
    }

    [Theory]
    [InlineData("WiseShell.App.csproj.user")]
    [InlineData("WiseShell.suo")]
    [InlineData("debug.log")]
    [InlineData("upload.tmp")]
    [InlineData("Thumbs.db")]
    [InlineData("Desktop.ini")]
    public void ShouldExcludeFile_SkipsLocalStateAndTemporaryFiles(string fileName)
    {
        var filePath = Path.Combine(@"D:\work\project", fileName);

        Assert.True(LocalUploadExclusions.ShouldExcludeFile(filePath));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("Program.cs")]
    [InlineData("terminal-host.html")]
    public void ShouldExcludeFile_KeepsSourceAndContentFiles(string fileName)
    {
        var filePath = Path.Combine(@"D:\work\project", fileName);

        Assert.False(LocalUploadExclusions.ShouldExcludeFile(filePath));
    }
}
