using System;
using System.Text.RegularExpressions;

namespace SDA.Desktop.Services
{
    public static class AccountFilter
    {
        public static bool Matches(string accountName, string query)
        {
            string name = accountName ?? "";
            if (string.IsNullOrEmpty(query))
            {
                return true;
            }

            if (query.StartsWith("~", StringComparison.Ordinal))
            {
                string pattern = query.Length == 1 ? "" : query.Substring(1);
                if (string.IsNullOrEmpty(pattern))
                {
                    return true;
                }

                try
                {
                    return Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException)
                {
                    return true;
                }
            }

            return name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
