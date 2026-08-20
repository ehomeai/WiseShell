using WiseShell.Core.Interfaces;

namespace WiseShell.Transport.SshNet.Services;

public sealed class SshNetSftpSessionFactory : ISftpSessionFactory
{
    private readonly ISshNetSftpClientFactory _clientFactory;

    public SshNetSftpSessionFactory(IHostKeyTrustStore hostKeyTrustStore)
        : this(new SshNetSftpClientFactory(hostKeyTrustStore))
    {
    }

    internal SshNetSftpSessionFactory(ISshNetSftpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public ISftpSession CreateSession()
    {
        return new SshNetSftpSession(_clientFactory);
    }
}
