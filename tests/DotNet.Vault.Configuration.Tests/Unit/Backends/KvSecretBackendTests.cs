using System.Net;
using System.Text;
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Http;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Backends;

public class KvSecretBackendTests
{
    [Fact]
    public async Task GetSecretsAsync_ForKvV2_UsesDataPathAndFlattensNestedData()
    {
        var observedRequest = new ObservedRequest();
        var handler = CreateHandler(
            HttpStatusCode.OK,
            "{\"data\":{\"data\":{\"username\":\"vault-user\",\"retries\":3}}}",
            observedRequest);
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        using var refresher = CreateRefresher();
        var backend = CreateBackend(httpClient, refresher, version: 2);

        var result = await backend.GetSecretsAsync(new SecretRequest { Path = "secret/data/application" });

        Assert.Equal("vault-user", result.Secrets["username"]);
        Assert.Equal("3", result.Secrets["retries"]);
        Assert.Equal(HttpMethod.Get, observedRequest.Method);
        Assert.Equal("/v1/secret/data/application", observedRequest.PathAndQuery);
    }

    [Fact]
    public async Task GetSecretsAsync_ForKvV1_UsesMountPathAndMapsDirectData()
    {
        var observedRequest = new ObservedRequest();
        var handler = CreateHandler(
            HttpStatusCode.OK,
            "{\"data\":{\"username\":\"legacy-user\",\"enabled\":true}}",
            observedRequest);
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        using var refresher = CreateRefresher();
        var backend = CreateBackend(httpClient, refresher, version: 1);

        var result = await backend.GetSecretsAsync(new SecretRequest { Path = "secret/application" });

        Assert.Equal("legacy-user", result.Secrets["username"]);
        Assert.Equal("True", result.Secrets["enabled"]);
        Assert.Equal(HttpMethod.Get, observedRequest.Method);
        Assert.Equal("/v1/secret/application", observedRequest.PathAndQuery);
    }

    [Fact]
    public async Task GetSecretsAsync_ForKvV2WithoutNestedData_ReturnsEmptySecrets()
    {
        var handler = CreateHandler(HttpStatusCode.OK, "{\"data\":{\"metadata\":{\"version\":2}}}");
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        using var refresher = CreateRefresher();
        var backend = CreateBackend(httpClient, refresher, version: 2);

        var result = await backend.GetSecretsAsync(new SecretRequest { Path = "secret/data/application" });

        Assert.Empty(result.Secrets);
    }

    [Fact]
    public async Task GetSecretsAsync_WhenVaultReturnsFailure_PropagatesHttpRequestException()
    {
        var handler = CreateHandler(HttpStatusCode.Forbidden, "{\"errors\":[\"permission denied\"]}");
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        using var refresher = CreateRefresher();
        var backend = CreateBackend(httpClient, refresher, version: 2);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => backend.GetSecretsAsync(new SecretRequest { Path = "secret/data/application" }));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task GetSecretsAsync_ForKvSecret_LeavesRefresherWithoutLeaseTracking()
    {
        var handler = CreateHandler(HttpStatusCode.OK, "{\"data\":{\"data\":{\"api-key\":\"value\"}}}");
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        using var refresher = CreateRefresher();
        var backend = CreateBackend(httpClient, refresher, version: 2);

        var result = await backend.GetSecretsAsync(new SecretRequest { Path = "secret/data/application" });

        Assert.Null(result.LeaseDuration);
        Assert.False(result.Renewable);
        Assert.Null(refresher.GetMinimumTtl());
    }

    private static Mock<HttpMessageHandler> CreateHandler(
        HttpStatusCode statusCode,
        string body,
        ObservedRequest? observedRequest = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                if (observedRequest is not null)
                {
                    observedRequest.Method = request.Method;
                    observedRequest.PathAndQuery = request.RequestUri!.PathAndQuery;
                }
            })
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        return handler;
    }

    private static KvSecretBackend CreateBackend(HttpClient httpClient, SecretRefresher refresher, int version)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName)).Returns(httpClient);

        return new KvSecretBackend(
            new KvSecretBackendOptions { BackendPath = "secret", Version = version },
            factory.Object,
            refresher);
    }

    private static SecretRefresher CreateRefresher()
    {
        return new SecretRefresher(
            new VaultOptions(),
            Mock.Of<ILogger<SecretRefresher>>(),
            Mock.Of<ISecretRefreshScheduler>(),
            new VaultLeaseRenewer(
                Mock.Of<IHttpClientFactory>(),
                Mock.Of<ILogger<VaultLeaseRenewer>>()));
    }

    private sealed class ObservedRequest
    {
        public HttpMethod Method { get; set; } = null!;

        public string PathAndQuery { get; set; } = string.Empty;
    }
}
