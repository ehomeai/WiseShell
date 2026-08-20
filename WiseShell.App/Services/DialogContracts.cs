using WiseShell.Core.Models;

namespace WiseShell.App.Services;

public sealed class SessionEditorRequest
{
    public required string Title { get; init; }

    public required SessionProfile Profile { get; init; }

    public bool HasStoredSecret { get; init; }

    public Func<SessionProfile, CancellationToken, Task<string?>>? BrowseRemoteDirectoryAsync { get; init; }
}

public sealed class SessionEditorResult
{
    public required SessionProfile Profile { get; init; }

    public string Secret { get; init; } = string.Empty;

    public bool SecretWasEdited { get; init; }
}

public sealed class SecretPromptRequest
{
    public required string Title { get; init; }

    public required string Message { get; init; }

    public required SessionProfile Profile { get; init; }

    public bool AllowEmpty { get; init; }

    public bool RememberSecret { get; init; }
}

public sealed class SecretPromptResult
{
    public string Secret { get; init; } = string.Empty;

    public bool RememberSecret { get; init; }
}

public sealed class TerminalDefaultsRequest
{
    public required string Title { get; init; }

    public double FontSize { get; init; }

    public string FontFamily { get; init; } = string.Empty;

    public bool ApplyToMatchingSessions { get; init; } = true;
}

public sealed class TerminalDefaultsResult
{
    public double FontSize { get; init; }

    public string FontFamily { get; init; } = string.Empty;

    public bool ApplyToMatchingSessions { get; init; }
}

public interface IDialogService
{
    Task<SessionEditorResult?> ShowSessionEditorAsync(SessionEditorRequest request, CancellationToken cancellationToken = default);

    Task<SecretPromptResult?> ShowSecretPromptAsync(SecretPromptRequest request, CancellationToken cancellationToken = default);

    Task<TerminalDefaultsResult?> ShowTerminalDefaultsAsync(TerminalDefaultsRequest request, CancellationToken cancellationToken = default);

    Task<string?> ShowTextPromptAsync(string title, string message, string initialValue = "", CancellationToken cancellationToken = default);

    Task<bool> ConfirmHostKeyAsync(HostKeyVerificationRequest request, CancellationToken cancellationToken = default);

    Task<bool> ConfirmDeleteAsync(string title, string message, CancellationToken cancellationToken = default);

    Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default);

    Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default);
}
