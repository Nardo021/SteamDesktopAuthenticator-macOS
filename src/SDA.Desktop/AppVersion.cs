using System.Reflection;

namespace SDA.Desktop
{
    public static class AppVersion
    {
        public static string Informational
        {
            get
            {
                AssemblyInformationalVersionAttribute attribute = typeof(AppVersion).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string value = attribute != null && !string.IsNullOrWhiteSpace(attribute.InformationalVersion)
                    ? attribute.InformationalVersion
                    : "0.0.0";
                int plus = value.IndexOf('+');
                if (plus >= 0)
                {
                    value = value.Substring(0, plus);
                }

                return value.Trim();
            }
        }

        public static string DisplayLabel
        {
            get { return "v" + Informational; }
        }
    }
}
