using WiseShell.Core.Models;

namespace WiseShell.Core.Interfaces;

public interface ITerminalSession : IAsyncDisposable
{
    event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    event EventHandler<TerminalConnectionStateChangedEventArgs>? ConnectionStateChanged;

    Task ConnectAsync(TerminalSessionStartRequest request, CancellationToken cancellationToken = default);

    Task SendInputAsync(string input, CancellationToken cancellationToken = default);

    Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}
