using WiseShell.Core.Models;

namespace WiseShell.Core.Interfaces;

public interface ISessionRepository
{
    Task<SessionWorkspace> LoadWorkspaceAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionProfile>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveWorkspaceAsync(SessionWorkspace workspace, CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyCollection<SessionProfile> profiles, CancellationToken cancellationToken = default);
}
