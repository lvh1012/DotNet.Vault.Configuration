using System.Net;
using System.Net.Http.Json;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Refresh;

public class SecretRefresherTests
{
    [Fact]
    public void Constructor_AcceptsVaultLeaseRenewerWithoutVaultClient()
    {
        var constructor = typeof(SecretRefresher).GetConstructor(
        [
            typeof(VaultOptions),
            typeof(ILogger<SecretRefresher>),
            typeof(ISecretRefreshScheduler),
            typeof(VaultLeaseRenewer)
        ]);

        Assert.NotNull(constructor);
    }

    [Fact]
    public async Task RenewableLease_IsRenewedBeforeRefreshSubscribersReload()
    {
        var operations = new List<string>();
        var scheduler = new ManualRefreshScheduler();
        using var refresher = CreateRefresher(
            scheduler,
            _ =>
            {
                operations.Add("renew");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { lease_duration = 600 })
                };
            });
        refresher.OnSecretsRefreshed += () =>
        {
            operations.Add("reload");
            return Task.CompletedTask;
        };
        refresher.TrackSecret(
            "database/creds/app",
            new SecretResult
            {
                LeaseId = "database/creds/app/lease",
                LeaseDuration = TimeSpan.FromMinutes(1),
                ExpireTime = DateTimeOffset.UtcNow.AddMinutes(-1),
                Renewable = true
            });

        await refresher.StartAsync(CancellationToken.None);
        await scheduler.TriggerAsync();

        Assert.Equal(["renew", "reload"], operations);
    }

    [Fact]
    public async Task FailedRenewal_MarksLeaseNonRenewableAndReloadsOnSubsequentCycles()
    {
        var renewalAttempts = 0;
        var reloads = 0;
        var scheduler = new ManualRefreshScheduler();
        using var refresher = CreateRefresher(
            scheduler,
            _ =>
            {
                renewalAttempts++;
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            });
        refresher.OnSecretsRefreshed += () =>
        {
            reloads++;
            return Task.CompletedTask;
        };
        refresher.TrackSecret(
            "database/creds/app",
            new SecretResult
            {
                LeaseId = "database/creds/app/lease",
                LeaseDuration = TimeSpan.FromMinutes(1),
                ExpireTime = DateTimeOffset.UtcNow.AddMinutes(-1),
                Renewable = true
            });

        await refresher.StartAsync(CancellationToken.None);
        await scheduler.TriggerAsync();
        await scheduler.TriggerAsync();

        Assert.Equal(1, renewalAttempts);
        Assert.Equal(2, reloads);
    }


    [Fact]
    public async Task NonRenewableLease_IsReloadedWithoutRenewal()
    {
        var reloads = 0;
        var scheduler = new ManualRefreshScheduler();
        using var refresher = CreateRefresher(
            scheduler,
            _ => throw new InvalidOperationException("Non-renewable leases must not be renewed."));
        refresher.OnSecretsRefreshed += () =>
        {
            reloads++;
            return Task.CompletedTask;
        };
        refresher.TrackSecret(
            "database/creds/app",
            new SecretResult
            {
                LeaseId = "database/creds/app/lease",
                LeaseDuration = TimeSpan.FromMinutes(1),
                ExpireTime = DateTimeOffset.UtcNow.AddMinutes(-1),
                Renewable = false
            });

        await refresher.StartAsync(CancellationToken.None);
        await scheduler.TriggerAsync();

        Assert.Equal(1, reloads);
    }
    private static SecretRefresher CreateRefresher(
        ManualRefreshScheduler scheduler,
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var httpClientFactory = new TestHttpClientFactory(responseFactory);
        return new SecretRefresher(
            new VaultOptions
            {
                Refresh = new VaultRefreshOptions
                {
                    Enabled = true,
                    Interval = TimeSpan.FromMinutes(1)
                }
            },
            Mock.Of<ILogger<SecretRefresher>>(),
            scheduler,
            new VaultLeaseRenewer(
                httpClientFactory,
                Mock.Of<ILogger<VaultLeaseRenewer>>()));
    }

    private sealed class ManualRefreshScheduler : ISecretRefreshScheduler
    {
        private Func<Task>? _refresh;

        public void Start(TimeSpan interval, Func<Task> refresh) => _refresh = refresh;

        public void Stop() => _refresh = null;

        public Task TriggerAsync() => _refresh?.Invoke() ?? Task.CompletedTask;

        public void Dispose() => _refresh = null;
    }

    private sealed class TestHttpClientFactory(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new TestHttpMessageHandler(responseFactory))
            {
                BaseAddress = new Uri("https://vault.example")
            };
    }

    private sealed class TestHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
