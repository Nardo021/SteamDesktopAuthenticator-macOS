using System;
using System.IO;

namespace SDA.Core.Tests
{
    internal static class FixtureFiles
    {
        public static string PathFor(string name)
        {
            return Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        }

        public static string Read(string name)
        {
            return File.ReadAllText(PathFor(name));
        }
    }
}
