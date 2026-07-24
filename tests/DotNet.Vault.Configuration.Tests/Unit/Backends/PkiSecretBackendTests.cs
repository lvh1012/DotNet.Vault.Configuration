using System.Net;
using System.Text;
using System.Text.Json;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Http;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Backends;

public sealed class PkiSecretBackendTests
{
    [Fact]
    public async Task GetSecretsAsync_IssuesCertificateRequestWithPayloadThatOmitsUnconfiguredTtl()
    {
        var observedRequest = new ObservedRequest();
        var handler = CreateHandler(HttpStatusCode.OK, "{\"data\":{}}", observedRequest);
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        using var refresher = CreateRefresher();
        var backend = CreateBackend(httpClient, refresher);

        await backend.GetSecretsAsync(new SecretRequest { Path = "pki/issue/api" });

        Assert.Equal(HttpMethod.Post, observedRequest.Method);
        Assert.Equal("/v1/pki/issue/api", observedRequest.PathAndQuery);
        using var payload = JsonDocument.Parse(observedRequest.Body);
        Assert.Equal("api.test", payload.RootElement.GetProperty("common_name").GetString());
        Assert.Equal("api.test,api.internal", payload.RootElement.GetProperty("alt_names").GetString());
        Assert.False(payload.RootElement.TryGetProperty("ttl", out _));
    }

    [Fact]
    public async Task GetSecretsAsync_MapsCertificateAndLeaseMetadataAndTracksLeaseTtl()
    {
        var handler = CreateHandler(
            HttpStatusCode.OK,
            "{\"lease_id\":\"pki/issue/api/lease\",\"lease_duration\":300,\"data\":{\"certificate\":\"certificate-pem\",\"private_key\":\"private-key-pem\",\"ca_chain\":[\"issuing-ca-pem\",\"root-ca-pem\"]}}");
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        using var refresher = CreateRefresher();
        var backend = CreateBackend(httpClient, refresher);

        var result = await backend.GetSecretsAsync(new SecretRequest { Path = "pki/issue/api" });

        Assert.Equal("certificate-pem", result.Secrets["certificate.pem"]);
        Assert.Equal("private-key-pem", result.Secrets["certificate.key"]);
        Assert.Equal($"issuing-ca-pem{Environment.NewLine}root-ca-pem", result.Secrets["certificate.ca_chain"]);
        Assert.Equal("pki/issue/api/lease", result.LeaseId);
        Assert.Equal(TimeSpan.FromMinutes(5), result.LeaseDuration);
        Assert.False(result.Renewable);
        Assert.NotNull(result.ExpireTime);
        Assert.Equal(TimeSpan.FromMinutes(5), refresher.GetMinimumTtl());
    }

    [Fact]
    public async Task GetSecretsAsync_WhenVaultDeniesCertificateRequest_PropagatesHttpRequestException()
    {
        var handler = CreateHandler(HttpStatusCode.Forbidden, "{\"errors\":[\"permission denied\"]}");
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        using var refresher = CreateRefresher();
        var backend = CreateBackend(httpClient, refresher);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => backend.GetSecretsAsync(new SecretRequest { Path = "pki/issue/api" }));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
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
                    observedRequest.Body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                }
            })
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        return handler;
    }

    private static PkiSecretBackend CreateBackend(HttpClient httpClient, SecretRefresher refresher)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName)).Returns(httpClient);

        return new PkiSecretBackend(
            new PkiSecretBackendOptions
            {
                BackendPath = "pki",
                Role = "api",
                CommonName = "api.test",
                AltNames = ["api.test", "api.internal"]
            },
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

        public string Body { get; set; } = string.Empty;
    }
}
