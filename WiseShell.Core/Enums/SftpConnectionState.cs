namespace WiseShell.Core.Enums;

public enum SftpConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Faulted = 4,
}
