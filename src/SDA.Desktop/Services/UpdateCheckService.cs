using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public enum UpdateCheckStatus
    {
        Current,
        UpdateAvailable,
        Unknown
    }

    public sealed class GitHubReleaseInfo
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; }

        [JsonProperty("html_url")]
        public string HtmlUrl { get; set; }
    }

    public sealed class UpdateCheckResult
    {
        public UpdateCheckResult(UpdateCheckStatus status, string currentVersion, string latestVersion, string releasePageUrl)
        {
            Status = status;
            CurrentVersion = currentVersion ?? "";
            LatestVersion = latestVersion ?? "";
            ReleasePageUrl = releasePageUrl ?? UpdateChannel.LatestPageUrl;
        }

        public UpdateCheckStatus Status { get; }

        public string CurrentVersion { get; }

        public string LatestVersion { get; }

        public string ReleasePageUrl { get; }
    }

    public static class UpdateChannel
    {
        public const string GitHubOwner = "Nardo021";
        public const string GitHubRepository = "SteamDesktopAuthenticator-macOS";
        public const string LatestApiUrl = "https://api.github.com/repos/Nardo021/SteamDesktopAuthenticator-macOS/releases/latest";
        public const string LatestPageUrl = "https://github.com/Nardo021/SteamDesktopAuthenticator-macOS/releases/latest";
        public const string ForbiddenUpstreamApiUrl = "https://api.github.com/repos/Jessecar96/SteamDesktopAuthenticator/releases/latest";
    }

    public static class UpdateVersion
    {
        public static bool TryParse(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string text = value.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(1);
            }

            int plus = text.IndexOf('+');
            if (plus >= 0)
            {
                text = text.Substring(0, plus);
            }

            int dash = text.IndexOf('-');
            if (dash >= 0)
            {
                text = text.Substring(0, dash);
            }

            if (text.IndexOf('.') < 0)
            {
                text += ".0";
            }

            return Version.TryParse(text, out version);
        }

        public static bool IsNewer(string latestTag, string current)
        {
            if (!TryParse(latestTag, out Version latest))
            {
                return false;
            }

            if (!TryParse(current, out Version now))
            {
                return false;
            }

            return latest > now;
        }
    }

    public interface IUpdateReleaseSource
    {
        Task<GitHubReleaseInfo> GetLatestAsync(CancellationToken cancellationToken);
    }

    public sealed class GitHubUpdateReleaseSource : IUpdateReleaseSource
    {
        public async Task<GitHubReleaseInfo> GetLatestAsync(CancellationToken cancellationToken)
        {
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamDesktopAuthenticator-macOS");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                using (HttpResponseMessage response = await client.GetAsync(UpdateChannel.LatestApiUrl, cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return null;
                    }

                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<GitHubReleaseInfo>(json);
                }
            }
        }
    }

    public sealed class UpdateCheckService
    {
        private readonly string _currentVersion;
        private readonly IUpdateReleaseSource _source;

        public UpdateCheckService(string currentVersion, IUpdateReleaseSource source)
        {
            _currentVersion = currentVersion ?? "";
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public static UpdateCheckResult Evaluate(string currentVersion, GitHubReleaseInfo release)
        {
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Unknown, currentVersion, null, UpdateChannel.LatestPageUrl);
            }

            string page = string.IsNullOrWhiteSpace(release.HtmlUrl) ? UpdateChannel.LatestPageUrl : release.HtmlUrl.Trim();
            if (UpdateVersion.IsNewer(release.TagName, currentVersion))
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, currentVersion, release.TagName.Trim(), page);
            }

            return new UpdateCheckResult(UpdateCheckStatus.Current, currentVersion, release.TagName.Trim(), page);
        }

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                GitHubReleaseInfo release = await _source.GetLatestAsync(cancellationToken).ConfigureAwait(false);
                return Evaluate(_currentVersion, release);
            }
            catch (Exception)
            {
                return new UpdateCheckResult(UpdateCheckStatus.Unknown, _currentVersion, null, UpdateChannel.LatestPageUrl);
            }
        }
    }
}
