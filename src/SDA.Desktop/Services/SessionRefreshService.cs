using SteamAuth;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface IAccessTokenRefresher
    {
        Task RefreshAsync(SessionData session, CancellationToken cancellationToken);
    }

    public sealed class SteamAccessTokenRefresher : IAccessTokenRefresher
    {
        public Task RefreshAsync(SessionData session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return session.RefreshAccessToken();
        }
    }

    public sealed class SessionRefreshService
    {
        private readonly IAccessTokenRefresher _refresher;

        public SessionRefreshService(IAccessTokenRefresher refresher)
        {
            _refresher = refresher ?? new SteamAccessTokenRefresher();
        }

        public bool CanForceRefresh(SessionData session)
        {
            return SessionStateInspector.CanForceRefresh(session);
        }

        public async Task RefreshAsync(SessionData session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _refresher.RefreshAsync(session, cancellationToken);
        }
    }
}
