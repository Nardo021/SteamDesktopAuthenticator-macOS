using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface IConfirmationIconLoader
    {
        Task<byte[]> LoadAsync(string url, CancellationToken cancellationToken);
    }

    public sealed class ConfirmationIconLoader : IConfirmationIconLoader
    {
        private const int MaxBytes = 1024 * 1024;
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            return client;
        }

        public async Task<byte[]> LoadAsync(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return null;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            try
            {
                using (HttpResponseMessage response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    if (data == null || data.Length == 0 || data.Length > MaxBytes)
                    {
                        return null;
                    }

                    return data;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
