using System.Net.Http;

namespace DotNet.Vault.Configuration.Http;

internal sealed class SingleHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public HttpClient CreateClient(string name) => _httpClient;
}
