using SteamAuth;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface ISteamClock
    {
        Task<long> GetSteamTimeAsync();
    }

    public sealed class SteamAuthClock : ISteamClock
    {
        public Task<long> GetSteamTimeAsync()
        {
            return TimeAligner.GetSteamTimeAsync();
        }
    }
}
