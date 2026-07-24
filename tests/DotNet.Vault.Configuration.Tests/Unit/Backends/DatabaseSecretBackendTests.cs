using System.Net;
using System.Text;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Http;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Backends;

public sealed class DatabaseSecretBackendTests
{
    [Fact]
    public async Task GetSecretsAsync_IssuesCredentialRequestAndMapsPrefixedCredentials()
    {
        var observedRequest = new ObservedRequest();
        var handler = CreateHandler(
            HttpStatusCode.OK,
            "{\"data\":{\"username\":\"readonly-user\",\"password\":\"s3cret\"}}",
            observedRequest);
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        using var refresher = CreateRefresher();
        var backend = CreateBackend(httpClient, refresher, propertyPrefix: "ConnectionStrings");

        var result = await backend.GetSecretsAsync(new SecretRequest { Path = "database/creds/readonly" });

        Assert.Equal(HttpMethod.Get, observedRequest.Method);
        Assert.Equal("/v1/database/creds/readonly", observedRequest.PathAndQuery);
        Assert.Equal("readonly-user", result.Secrets["ConnectionStrings.username"]);
        Assert.Equal("s3cret", result.Secrets["ConnectionStrings.password"]);
    }

    [Fact]
    public async Task GetSecretsAsync_ForRenewableLease_MapsLeaseMetadataAndTracksTtl()
    {
        var handler = CreateHandler(
            HttpStatusCode.OK,
            "{\"lease_id\":\"database/creds/readonly/lease\",\"lease_duration\":300,\"renewable\":true,\"data\":{\"username\":\"readonly-user\",\"password\":\"s3cret\"}}");
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        using var refresher = CreateRefresher();
        var backend = CreateBackend(httpClient, refresher);

        var result = await backend.GetSecretsAsync(new SecretRequest { Path = "database/creds/readonly" });

        Assert.Equal("database/creds/readonly/lease", result.LeaseId);
        Assert.Equal(TimeSpan.FromMinutes(5), result.LeaseDuration);
        Assert.True(result.Renewable);
        Assert.NotNull(result.ExpireTime);
        Assert.Equal(TimeSpan.FromMinutes(5), refresher.GetMinimumTtl());
    }

    [Fact]
    public async Task GetSecretsAsync_WhenVaultDeniesCredentialRequest_PropagatesHttpRequestException()
    {
        var handler = CreateHandler(HttpStatusCode.Forbidden, "{\"errors\":[\"permission denied\"]}");
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        using var refresher = CreateRefresher();
        var backend = CreateBackend(httpClient, refresher);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => backend.GetSecretsAsync(new SecretRequest { Path = "database/creds/readonly" }));

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
                }
            })
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        return handler;
    }

    private static DatabaseSecretBackend CreateBackend(
        HttpClient httpClient,
        SecretRefresher refresher,
        string? propertyPrefix = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName)).Returns(httpClient);

        return new DatabaseSecretBackend(
            new DatabaseSecretBackendOptions
            {
                BackendPath = "database",
                PropertyPrefix = propertyPrefix
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
    }
}
