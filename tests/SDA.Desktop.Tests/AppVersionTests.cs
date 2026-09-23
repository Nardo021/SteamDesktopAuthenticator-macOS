using SDA.Desktop;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class AppVersionTests
    {
        [Fact]
        public void Informational_IsCentralizedProductVersion()
        {
            Assert.Equal("1.0.4", AppVersion.Informational);
            Assert.Equal("v1.0.4", AppVersion.DisplayLabel);
        }
    }
}
