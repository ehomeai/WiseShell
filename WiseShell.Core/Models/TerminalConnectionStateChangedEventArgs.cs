using WiseShell.Core.Enums;

namespace WiseShell.Core.Models;

public sealed class TerminalConnectionStateChangedEventArgs : EventArgs
{
    public TerminalConnectionStateChangedEventArgs(TerminalConnectionState state, string? message = null)
    {
        State = state;
        Message = message;
    }

    public TerminalConnectionState State { get; }

    public string? Message { get; }
}
