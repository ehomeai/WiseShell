namespace WiseShell.Tests;

public sealed class IntegrationStubsTests
{
    [Fact(Skip = "需要本地或测试用 OpenSSH 服务端后手动启用。")]
    public Task PasswordLogin_StreamsTerminalOutput()
    {
        return Task.CompletedTask;
    }

    [Fact(Skip = "需要带私钥与口令的测试账户后手动启用。")]
    public Task PrivateKeyLogin_StreamsTerminalOutput()
    {
        return Task.CompletedTask;
    }

    [Fact(Skip = "需要可写的 SFTP 测试目录后手动启用。")]
    public Task SftpUploadDownloadAndDelete_WorkAsExpected()
    {
        return Task.CompletedTask;
    }
}
