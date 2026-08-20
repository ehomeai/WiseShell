using WiseShell.Core.Enums;

namespace WiseShell.Core.Models;

public sealed class SftpConnectionStateChangedEventArgs : EventArgs
{
    public SftpConnectionStateChangedEventArgs(SftpConnectionState state, string? message = null)
    {
        State = state;
        Message = message;
    }

    public SftpConnectionState State { get; }

    public string? Message { get; }
}
