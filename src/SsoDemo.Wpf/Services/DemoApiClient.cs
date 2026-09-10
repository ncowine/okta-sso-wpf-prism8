using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Authentication.Okta;

namespace SsoDemo.Wpf.Services
{
    /// <summary>
    /// Thin <see cref="HttpClient"/> wrapper for the demo API. The bearer token is added by
    /// <see cref="BearerTokenHandler"/>; responses are returned as pretty-printed JSON for display.
    /// </summary>
    public sealed class DemoApiClient : IDemoApiClient, IDisposable
    {
        private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

        private readonly HttpClient http;

        public DemoApiClient(IOktaAuthenticationService authentication, DemoApiOptions options)
        {
            BaseUrl = options.BaseUrl.TrimEnd('/');
            http = new HttpClient(new BearerTokenHandler(authentication) { InnerHandler = new HttpClientHandler() })
            {
                BaseAddress = new Uri(BaseUrl + "/"),
                Timeout = TimeSpan.FromSeconds(30),
            };
        }

        public string BaseUrl { get; }

        public Task<string> GetProfileAsync(CancellationToken cancellationToken = default) =>
            GetAsync("api/profile", cancellationToken);

        public Task<string> GetOrdersAsync(CancellationToken cancellationToken = default) =>
            GetAsync("api/orders", cancellationToken);

        public Task<string> GetPartnerViaExchangeAsync(CancellationToken cancellationToken = default) =>
            GetAsync("api/partner", cancellationToken);

        private async Task<string> GetAsync(string path, CancellationToken cancellationToken)
        {
            Debug.WriteLine($"[DemoApiClient] GET {BaseUrl}/{path}");
            using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from /{path}" +
                    (string.IsNullOrWhiteSpace(body) ? string.Empty : $"\n{body}"));
            }

            return Prettify(body);
        }

        private static string Prettify(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(document.RootElement, Pretty);
            }
            catch (JsonException)
            {
                return json;
            }
        }

        public void Dispose() => http.Dispose();
    }
}
