using CommandLine;

namespace SDA.Core.Common
{
    public static class CommandLineStartup
    {
        public static CommandLineOptions Parse(string[] args)
        {
            CommandLineOptions parsed = new CommandLineOptions();
            if (args == null || args.Length == 0)
            {
                return parsed;
            }

            Parser parser = new Parser(settings =>
            {
                settings.HelpWriter = null;
                settings.AutoHelp = false;
                settings.AutoVersion = false;
            });

            parser.ParseArguments<CommandLineOptions>(args)
                .WithParsed(options => parsed = options);

            return parsed;
        }
    }
}
