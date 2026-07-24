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
    public void TrackSecret_WithLease_ExposesItsTtlForScheduling()
    {
        using var refresher = CreateRefresher(new ManualRefreshScheduler());

        refresher.TrackSecret(
            "database/creds/app",
            new SecretResult { LeaseDuration = TimeSpan.FromMinutes(5) });

        Assert.Equal(TimeSpan.FromMinutes(5), refresher.GetMinimumTtl());
    }

    [Fact]
    public void TrackSecret_WithoutLease_DoesNotCreateRefreshWork()
    {
        using var refresher = CreateRefresher(new ManualRefreshScheduler());

        refresher.TrackSecret("kv/app", new SecretResult());

        Assert.Null(refresher.GetMinimumTtl());
        Assert.False(refresher.ShouldRefresh());
    }

    [Fact]
    public async Task StartAsync_WithConfiguredInterval_SchedulesAtConfiguredInterval()
    {
        var scheduler = new ManualRefreshScheduler();
        using var refresher = CreateRefresher(
            scheduler,
            options: EnabledOptions(TimeSpan.FromMinutes(3)));

        await refresher.StartAsync(CancellationToken.None);

        Assert.Equal(1, scheduler.StartCount);
        Assert.Equal(TimeSpan.FromMinutes(3), scheduler.StartInterval);
    }

    [Fact]
    public async Task StartAsync_WithoutConfiguredInterval_SchedulesAtEightyPercentOfShortestTrackedTtl()
    {
        var scheduler = new ManualRefreshScheduler();
        using var refresher = CreateRefresher(
            scheduler,
            options: EnabledOptions(interval: null));
        refresher.TrackSecret(
            "database/creds/app",
            new SecretResult { LeaseDuration = TimeSpan.FromMinutes(10) });
        refresher.TrackSecret(
            "pki/issue/app",
            new SecretResult { LeaseDuration = TimeSpan.FromMinutes(5) });

        await refresher.StartAsync(CancellationToken.None);

        Assert.Equal(1, scheduler.StartCount);
        Assert.Equal(TimeSpan.FromMinutes(4), scheduler.StartInterval);
    }

    [Fact]
    public async Task StartAsync_WithoutConfiguredIntervalOrTrackedTtl_DoesNotSchedule()
    {
        var scheduler = new ManualRefreshScheduler();
        using var refresher = CreateRefresher(
            scheduler,
            options: EnabledOptions(interval: null));

        await refresher.StartAsync(CancellationToken.None);

        Assert.Equal(0, scheduler.StartCount);
        Assert.Null(scheduler.StartInterval);
    }

    [Fact]
    public async Task ScheduledCycle_WhenNoLeaseIsNearExpiry_DoesNotReloadSecrets()
    {
        var scheduler = new ManualRefreshScheduler();
        using var refresher = CreateRefresher(scheduler);
        var reloads = 0;
        refresher.OnSecretsRefreshed += () =>
        {
            reloads++;
            return Task.CompletedTask;
        };
        refresher.TrackSecret(
            "database/creds/app",
            new SecretResult
            {
                LeaseDuration = TimeSpan.FromMinutes(5),
                ExpireTime = DateTimeOffset.UtcNow.AddMinutes(4)
            });

        await refresher.StartAsync(CancellationToken.None);
        await scheduler.TriggerAsync();

        Assert.Equal(0, reloads);
    }

    [Fact]
    public async Task ScheduledCycle_WhenLeaseIsNearExpiry_ReloadsSecrets()
    {
        var scheduler = new ManualRefreshScheduler();
        using var refresher = CreateRefresher(scheduler);
        var reloads = 0;
        refresher.OnSecretsRefreshed += () =>
        {
            reloads++;
            return Task.CompletedTask;
        };
        refresher.TrackSecret(
            "database/creds/app",
            ExpiredLease());

        await refresher.StartAsync(CancellationToken.None);
        await scheduler.TriggerAsync();

        Assert.Equal(1, reloads);
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
        refresher.TrackSecret("database/creds/app", ExpiredLease(renewable: true));

        await refresher.StartAsync(CancellationToken.None);
        await scheduler.TriggerAsync();

        Assert.Equal(["renew", "reload"], operations);
    }

    [Fact]
    public async Task FailedRenewal_MarksLeaseNonRenewableAndContinuesReloading()
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
        refresher.TrackSecret("database/creds/app", ExpiredLease(renewable: true));

        await refresher.StartAsync(CancellationToken.None);
        await scheduler.TriggerAsync();
        await scheduler.TriggerAsync();

        Assert.Equal(1, renewalAttempts);
        Assert.Equal(2, reloads);
    }

    [Fact]
    public async Task StartAsync_WhenRefreshIsDisabled_DoesNotScheduleOrReload()
    {
        var scheduler = new ManualRefreshScheduler();
        using var refresher = CreateRefresher(
            scheduler,
            options: new VaultOptions
            {
                Refresh = new VaultRefreshOptions
                {
                    Enabled = false,
                    Interval = TimeSpan.FromMinutes(1)
                }
            });
        var reloads = 0;
        refresher.OnSecretsRefreshed += () =>
        {
            reloads++;
            return Task.CompletedTask;
        };
        refresher.TrackSecret("database/creds/app", ExpiredLease());

        await refresher.StartAsync(CancellationToken.None);
        await scheduler.TriggerAsync();

        Assert.Equal(0, scheduler.StartCount);
        Assert.Equal(0, reloads);
    }

    [Fact]
    public async Task Dispose_PreventsPostDisposalScheduledReloads()
    {
        var scheduler = new ManualRefreshScheduler();
        var refresher = CreateRefresher(scheduler);
        var reloads = 0;
        refresher.OnSecretsRefreshed += () =>
        {
            reloads++;
            return Task.CompletedTask;
        };
        refresher.TrackSecret("database/creds/app", ExpiredLease());

        await refresher.StartAsync(CancellationToken.None);
        refresher.Dispose();
        await scheduler.TriggerAsync();

        Assert.True(scheduler.IsDisposed);
        Assert.Equal(0, reloads);
    }

    private static VaultOptions EnabledOptions(TimeSpan? interval = null) =>
        new()
        {
            Refresh = new VaultRefreshOptions
            {
                Enabled = true,
                Interval = interval
            }
        };

    private static SecretResult ExpiredLease(bool renewable = false) =>
        new()
        {
            LeaseId = "database/creds/app/lease",
            LeaseDuration = TimeSpan.FromMinutes(1),
            ExpireTime = DateTimeOffset.UtcNow.AddMinutes(-1),
            Renewable = renewable
        };

    private static SecretRefresher CreateRefresher(
        ManualRefreshScheduler scheduler,
        Func<HttpRequestMessage, HttpResponseMessage>? responseFactory = null,
        VaultOptions? options = null)
    {
        var httpClientFactory = new TestHttpClientFactory(
            responseFactory ?? (_ => throw new InvalidOperationException("Unexpected lease renewal.")));
        return new SecretRefresher(
            options ?? EnabledOptions(TimeSpan.FromMinutes(1)),
            Mock.Of<ILogger<SecretRefresher>>(),
            scheduler,
            new VaultLeaseRenewer(
                httpClientFactory,
                Mock.Of<ILogger<VaultLeaseRenewer>>()));
    }

    private sealed class ManualRefreshScheduler : ISecretRefreshScheduler
    {
        private Func<Task>? _refresh;

        public int StartCount { get; private set; }

        public TimeSpan? StartInterval { get; private set; }

        public bool IsDisposed { get; private set; }

        public void Start(TimeSpan interval, Func<Task> refresh)
        {
            StartCount++;
            StartInterval = interval;
            _refresh = refresh;
        }

        public void Stop() => _refresh = null;

        public Task TriggerAsync() => _refresh?.Invoke() ?? Task.CompletedTask;

        public void Dispose()
        {
            IsDisposed = true;
            _refresh = null;
        }
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
