using WiseShell.Core.Infrastructure;

namespace WiseShell.Tests;

public sealed class RemotePathHelperTests
{
    [Theory]
    [InlineData("/home/tester", "logs", "/home/tester/logs")]
    [InlineData("/home/tester", "./logs", "/home/tester/logs")]
    [InlineData("/home/tester", "../shared", "/home/shared")]
    [InlineData("/home/tester", "/srv/data", "/srv/data")]
    public void Resolve_NormalizesRemotePaths(string basePath, string input, string expected)
    {
        var resolved = RemotePathHelper.Resolve(basePath, input);

        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("/home/tester/logs", "/home/tester")]
    [InlineData("/home/tester", "/home")]
    [InlineData("/home", "/")]
    [InlineData("/", "/")]
    public void GetParent_ReturnsNormalizedRemoteParent(string path, string expected)
    {
        var parent = RemotePathHelper.GetParent(path);

        Assert.Equal(expected, parent);
    }

    [Fact]
    public void Combine_UsesCurrentRemoteDirectory()
    {
        var combined = RemotePathHelper.Combine("/srv/app", "deploy.log");

        Assert.Equal("/srv/app/deploy.log", combined);
    }
}
