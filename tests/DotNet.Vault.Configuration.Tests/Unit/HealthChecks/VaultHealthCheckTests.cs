using System.Net;
using System.Net.Http;
using System.Text;
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.HealthChecks;
using DotNet.Vault.Configuration.Http;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.HealthChecks;

public class VaultHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenVaultIsActiveAndAuthenticated_ReturnsHealthyWithVaultDiagnostics()
    {
        using var fixture = CreateFixture(HttpStatusCode.OK, ActiveHealthResponse);

        var result = await fixture.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Vault is healthy", result.Description);
        Assert.Equal("1.17.0", result.Data["vault_version"]);
        Assert.Equal("primary", result.Data["vault_cluster"]);
        Assert.Equal(ServerTime, result.Data["vault_server_time"]);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenVaultIsStandby_ReturnsHealthy()
    {
        using var fixture = CreateFixture(
            HttpStatusCode.TooManyRequests,
            "{\"initialized\":true,\"sealed\":false,\"standby\":true,\"version\":\"1.17.0\",\"cluster_name\":\"primary\",\"server_time_utc\":\"2026-07-25T12:00:00Z\"}");

        var result = await fixture.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Vault is healthy", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenVaultIsNotInitialized_ReturnsUnhealthy()
    {
        using var fixture = CreateFixture(
            HttpStatusCode.NotImplemented,
            "{\"initialized\":false,\"sealed\":true,\"standby\":false}");

        var result = await fixture.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Vault is not initialized", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenVaultIsSealed_ReturnsUnhealthy()
    {
        using var fixture = CreateFixture(
            HttpStatusCode.ServiceUnavailable,
            "{\"initialized\":true,\"sealed\":true,\"standby\":false}");

        var result = await fixture.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Vault is sealed", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenVaultAuthenticationIsInvalid_ReturnsDegraded()
    {
        using var fixture = CreateFixture(HttpStatusCode.OK, ActiveHealthResponse, HttpStatusCode.Unauthorized);

        var result = await fixture.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("Vault authentication is invalid or expired", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTrackedSecretExpiresInUnderFiveMinutes_ReturnsDegraded()
    {
        using var fixture = CreateFixture(HttpStatusCode.OK, ActiveHealthResponse);
        fixture.Refresher.TrackSecret("database/creds", new SecretResult
        {
            LeaseId = "database/creds/lease",
            LeaseDuration = TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59),
            Renewable = true
        });

        var result = await fixture.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("Some secrets are expiring soon", result.Description);
        Assert.Equal("00:04:59", result.Data["minimum_secret_ttl"]);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenCancellationIsRequested_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        using var fixture = CreateFixture(
            HttpStatusCode.OK,
            ActiveHealthResponse,
            requestHandler: (_, cancellationToken) => Task.FromCanceled<HttpResponseMessage>(cancellationToken));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.HealthCheck.CheckHealthAsync(new HealthCheckContext(), cancellationSource.Token));
    }

    private static readonly DateTimeOffset ServerTime = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    private const string ActiveHealthResponse = "{\"initialized\":true,\"sealed\":false,\"standby\":false,\"version\":\"1.17.0\",\"cluster_name\":\"primary\",\"cluster_id\":\"cluster-id\",\"server_time_utc\":\"2026-07-25T12:00:00Z\"}";

    private static HealthCheckFixture CreateFixture(
        HttpStatusCode healthStatus,
        string healthResponse,
        HttpStatusCode authenticationStatus = HttpStatusCode.OK,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? requestHandler = null)
    {
        var handler = new DelegateHttpMessageHandler(requestHandler ?? ((request, _) =>
        {
            return request.RequestUri!.AbsolutePath switch
            {
                "/v1/sys/health" => Task.FromResult(JsonResponse(healthStatus, healthResponse)),
                "/v1/auth/token/lookup-self" => Task.FromResult(new HttpResponseMessage(authenticationStatus)),
                _ => throw new InvalidOperationException($"Unexpected Vault request path: {request.RequestUri!.AbsolutePath}")
            };
        }));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://vault.test") };
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName)).Returns(httpClient);

        var options = new VaultOptions
        {
            Uri = new Uri("https://vault.test"),
            Authentication = new VaultAuthenticationConfiguration { Method = "token" }
        };
        var authProvider = new Mock<IVaultAuthenticationProvider>();
        authProvider.SetupGet(provider => provider.AuthenticationMethod).Returns("token");
        authProvider.Setup(provider => provider.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("test-token");
        var client = new VaultClient(
            httpClientFactory.Object,
            options,
            [authProvider.Object],
            [],
            Mock.Of<ILogger<VaultClient>>());
        var refresher = new SecretRefresher(
            options,
            Mock.Of<ILogger<SecretRefresher>>(),
            Mock.Of<ISecretRefreshScheduler>(),
            new VaultLeaseRenewer(Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<VaultLeaseRenewer>>()));

        return new HealthCheckFixture(
            new VaultHealthCheck(client, refresher, options, Mock.Of<ILogger<VaultHealthCheck>>()),
            refresher,
            httpClient);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private sealed class HealthCheckFixture : IDisposable
    {
        public HealthCheckFixture(VaultHealthCheck healthCheck, SecretRefresher refresher, HttpClient httpClient)
        {
            HealthCheck = healthCheck;
            Refresher = refresher;
            _httpClient = httpClient;
        }

        public VaultHealthCheck HealthCheck { get; }

        public SecretRefresher Refresher { get; }

        private readonly HttpClient _httpClient;

        public void Dispose()
        {
            _httpClient.Dispose();
            Refresher.Dispose();
        }
    }

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _sendAsync(request, cancellationToken);
        }
    }
}
