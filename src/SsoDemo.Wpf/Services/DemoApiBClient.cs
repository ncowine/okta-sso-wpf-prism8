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
    /// Way 1 of reaching DemoApiB: the client calls it directly with the user's access token
    /// (whose <c>aud</c> already includes <c>api://demo-api-b</c>). No token exchange, no actor.
    /// </summary>
    public sealed class DemoApiBClient : IDemoApiBClient, IDisposable
    {
        private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

        private readonly HttpClient http;

        public DemoApiBClient(IOktaAuthenticationService authentication, DemoApiOptions options)
        {
            BaseUrl = options.BaseUrlB.TrimEnd('/');
            http = new HttpClient(new BearerTokenHandler(authentication) { InnerHandler = new HttpClientHandler() })
            {
                BaseAddress = new Uri(BaseUrl + "/"),
                Timeout = TimeSpan.FromSeconds(30),
            };
        }

        public string BaseUrl { get; }

        public async Task<string> GetResourceAsync(CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"[DemoApiBClient] GET {BaseUrl}/b/resource (direct)");
            using var response = await http.GetAsync("b/resource", cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from /b/resource" +
                    (string.IsNullOrWhiteSpace(body) ? string.Empty : $"\n{body}"));
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                return JsonSerializer.Serialize(document.RootElement, Pretty);
            }
            catch (JsonException)
            {
                return body;
            }
        }

        public void Dispose() => http.Dispose();
    }
}
