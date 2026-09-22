using SDA.Desktop.Services;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class UpdateCheckTests
    {
        [Fact]
        public void Channel_TargetsThisRepositoryNotJessecar96()
        {
            Assert.Contains("Nardo021/SteamDesktopAuthenticator-macOS", UpdateChannel.LatestApiUrl);
            Assert.Contains("Nardo021/SteamDesktopAuthenticator-macOS", UpdateChannel.LatestPageUrl);
            Assert.DoesNotContain("Jessecar96", UpdateChannel.LatestApiUrl);
            Assert.DoesNotContain("Jessecar96", UpdateChannel.LatestPageUrl);
            Assert.Equal("https://api.github.com/repos/Jessecar96/SteamDesktopAuthenticator/releases/latest", UpdateChannel.ForbiddenUpstreamApiUrl);
            Assert.NotEqual(UpdateChannel.ForbiddenUpstreamApiUrl, UpdateChannel.LatestApiUrl);
        }

        [Theory]
        [InlineData("1.0.1", "1.0.0", true)]
        [InlineData("v1.2.0", "1.1.9", true)]
        [InlineData("1.0.0", "1.0.0", false)]
        [InlineData("v1.0.0", "1.0.1", false)]
        [InlineData("", "1.0.0", false)]
        public void IsNewer_ComparesSemanticVersions(string latest, string current, bool expected)
        {
            Assert.Equal(expected, UpdateVersion.IsNewer(latest, current));
        }

        [Fact]
        public void Evaluate_UpdateAvailableUsesReleasePage()
        {
            GitHubReleaseInfo release = new GitHubReleaseInfo
            {
                TagName = "v1.1.0",
                HtmlUrl = "https://github.com/Nardo021/SteamDesktopAuthenticator-macOS/releases/tag/v1.1.0"
            };

            UpdateCheckResult result = UpdateCheckService.Evaluate("1.0.0", release);
            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal("1.0.0", result.CurrentVersion);
            Assert.Equal("v1.1.0", result.LatestVersion);
            Assert.Equal(release.HtmlUrl, result.ReleasePageUrl);
        }

        [Fact]
        public void Evaluate_MissingReleaseIsUnknown()
        {
            UpdateCheckResult result = UpdateCheckService.Evaluate("1.0.0", null);
            Assert.Equal(UpdateCheckStatus.Unknown, result.Status);
            Assert.Equal(UpdateChannel.LatestPageUrl, result.ReleasePageUrl);
        }

        [Fact]
        public void ReleasePageOpener_RejectsForeignUrls()
        {
            Assert.False(ReleasePageOpener.TryOpen("https://api.github.com/repos/Jessecar96/SteamDesktopAuthenticator/releases/latest"));
            Assert.False(ReleasePageOpener.TryOpen("https://example.com/malware"));
            Assert.False(ReleasePageOpener.TryOpen(null));
        }
    }
}
