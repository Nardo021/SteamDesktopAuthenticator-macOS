using CommandLine;
using SDA.Core.Common;
using Xunit;

namespace SDA.Core.Tests
{
    public class CommandLineOptionsTests
    {
        [Fact]
        public void ShortFlags_SetEncryptionKeyAndSilent()
        {
            CommandLineOptions options = Parse("-k", "fixture-key", "-s");

            Assert.Equal("fixture-key", options.EncryptionKey);
            Assert.True(options.Silent);
        }

        [Fact]
        public void LongFlags_SetEncryptionKeyAndSilent()
        {
            CommandLineOptions options = Parse("--encryption-key", "fixture-key", "--silent");

            Assert.Equal("fixture-key", options.EncryptionKey);
            Assert.True(options.Silent);
        }

        [Fact]
        public void MissingFlags_LeaveDefaults()
        {
            CommandLineOptions options = Parse();

            Assert.Null(options.EncryptionKey);
            Assert.False(options.Silent);
        }

        private static CommandLineOptions Parse(params string[] args)
        {
            CommandLineOptions options = null;
            Parser.Default.ParseArguments<CommandLineOptions>(args)
                .WithParsed(parsed => options = parsed)
                .WithNotParsed(errors => throw new Xunit.Sdk.XunitException("Command line parse failed."));
            return options;
        }
    }
}
