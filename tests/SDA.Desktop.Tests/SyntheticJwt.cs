using System;
using System.Text;

namespace SDA.Desktop.Tests
{
    internal static class SyntheticJwt
    {
        public static string Create(long expiresAtUnixSeconds)
        {
            string header = Base64Url("{\"alg\":\"none\"}");
            string payload = Base64Url("{\"exp\":" + expiresAtUnixSeconds + "}");
            return header + "." + payload + ".synthetic";
        }

        public static string Valid()
        {
            return Create(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600);
        }

        public static string Expired()
        {
            return Create(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 120);
        }

        private static string Base64Url(string json)
        {
            string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
