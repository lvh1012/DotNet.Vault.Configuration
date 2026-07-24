using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Extensions;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Core;

public class VaultConfigurationSourceTests
{
    [Fact]
    public async Task AddVault_WithLeasedSecretAndNoConfiguredInterval_SchedulesSharedRefresherAtEightyPercentOfTtl_AndStopsAfterDisposal()
    {
        var scheduler = new ManualRefreshScheduler();
        var backend = new Mock<IVaultSecretBackend>(MockBehavior.Strict);
        SecretRefresher? refresher = null;
        var backendCalls = 0;

        backend.Setup(backend => backend.CanHandle("database/creds/app")).Returns(true);
        backend
            .Setup(backend => backend.GetSecretsAsync(It.IsAny<SecretRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var call = Interlocked.Increment(ref backendCalls);
                var result = new SecretResult
                {
                    Secrets = new Dictionary<string, string>
                    {
                        ["credential"] = call == 1 ? "initial" : "refreshed"
                    },
                    LeaseDuration = TimeSpan.FromMinutes(1),
                    ExpireTime = call == 1
                        ? DateTimeOffset.UtcNow.AddMinutes(-1)
                        : DateTimeOffset.UtcNow.AddHours(1)
                };
                refresher!.TrackSecret("database/creds/app", result);
                return Task.FromResult(result);
            });

        var builder = new ConfigurationBuilder()
            .AddVault(options =>
            {
                options.Database = new DatabaseSecretBackendOptions
                {
                    Enabled = true,
                    BackendPath = "database",
                    Role = "app"
                };
                options.Refresh = new VaultRefreshOptions
                {
                    Enabled = true
                };
            });
        var source = Assert.IsType<VaultConfigurationSource>(Assert.Single(builder.Sources));
        source.ServiceProviderFactory = () =>
        {
            refresher = new SecretRefresher(
                source.Options,
                NullLogger<SecretRefresher>.Instance,
                scheduler,
                new VaultLeaseRenewer(
                    Mock.Of<IHttpClientFactory>(),
                    NullLogger<VaultLeaseRenewer>.Instance));
            var client = new VaultClient(
                Mock.Of<IHttpClientFactory>(),
                source.Options,
                [],
                [backend.Object],
                NullLogger<VaultClient>.Instance);
            return new SourceOwnedServiceProvider(client, refresher);
        };

        var configuration = builder.Build();
        using var configurationLifetime = Assert.IsAssignableFrom<IDisposable>(configuration);
        var provider = Assert.IsType<VaultConfigurationProvider>(Assert.Single(configuration.Providers));
        var reloadCount = 0;
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = configuration.GetReloadToken().RegisterChangeCallback(_ =>
        {
            if (Interlocked.Increment(ref reloadCount) == 1)
                reloaded.TrySetResult();
        }, null);

        Assert.Equal(1, scheduler.StartCount);
        Assert.Equal(TimeSpan.FromSeconds(48), scheduler.StartInterval);
        Assert.Equal("initial", configuration["credential"]);

        await scheduler.TriggerAsync();
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("refreshed", configuration["credential"]);
        Assert.Equal(1, reloadCount);
        Assert.Equal(2, backendCalls);

        provider.Dispose();
        configurationLifetime.Dispose();

        await scheduler.TriggerAsync();

        Assert.True(scheduler.IsDisposed);
        Assert.Equal(1, reloadCount);
        Assert.Equal(2, backendCalls);
    }

    [Fact]
    public void Build_WithNonDisposableServiceProviderFactoryResult_ThrowsDeterministicOwnershipError()
    {
        var source = new VaultConfigurationSource
        {
            ServiceProviderFactory = () => new NonDisposableServiceProvider()
        };

        var exception = Assert.Throws<InvalidOperationException>(() => source.Build(new ConfigurationBuilder()));

        Assert.Contains("ServiceProviderFactory must return an IDisposable service provider.", exception.Message);
    }

    [Fact]
    public void Build_DefaultServices_RegistersVaultLeaseRenewerForSecretRefresher()
    {
        var builder = new ConfigurationBuilder()
            .AddVault(_ => { });

        var configuration = builder.Build();
        using var configurationLifetime = Assert.IsAssignableFrom<IDisposable>(configuration);

        Assert.NotNull(configuration);
    }

    [Fact]
    public void Build_WithServiceProviderFactory_InvokesFactoryOnceAndReturnsProvider()
    {
        var factoryCalls = 0;
        var source = new VaultConfigurationSource();
        source.ServiceProviderFactory = () =>
        {
            factoryCalls++;
            return CreateServiceProvider(source.Options);
        };

        using var provider = Assert.IsType<VaultConfigurationProvider>(
            source.Build(new ConfigurationBuilder()));

        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void Build_WithServiceProviderFactory_DisposesSourceOwnedProviderWhenConfigurationProviderDisposes()
    {
        var source = new VaultConfigurationSource();
        var serviceProvider = CreateServiceProvider(source.Options);
        source.ServiceProviderFactory = () => serviceProvider;

        var provider = Assert.IsType<VaultConfigurationProvider>(
            source.Build(new ConfigurationBuilder()));

        provider.Dispose();

        Assert.True(serviceProvider.IsDisposed);
    }

    [Fact]
    public void Build_DefaultServices_WithTokenAuthenticationAndNoBackends_LoadsEmptyConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .AddVault(options => options.Authentication.Token = new TokenAuthenticationOptions
            {
                Token = "test-token"
            });

        var configuration = builder.Build();
        using var configurationLifetime = Assert.IsAssignableFrom<IDisposable>(configuration);

        Assert.DoesNotContain(configuration.AsEnumerable(), pair => pair.Value is not null);
    }

    private static SourceOwnedServiceProvider CreateServiceProvider(VaultOptions options)
    {
        var refresher = new SecretRefresher(
            options,
            NullLogger<SecretRefresher>.Instance,
            new ManualRefreshScheduler(),
            new VaultLeaseRenewer(
                Mock.Of<IHttpClientFactory>(),
                NullLogger<VaultLeaseRenewer>.Instance));
        var client = new VaultClient(
            Mock.Of<IHttpClientFactory>(),
            options,
            [],
            [],
            NullLogger<VaultClient>.Instance);

        return new SourceOwnedServiceProvider(client, refresher);
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
            _refresh = refresh;
            StartInterval = interval;
        }

        public void Stop()
        {
            _refresh = null;
        }

        public Task TriggerAsync() => _refresh?.Invoke() ?? Task.CompletedTask;

        public void Dispose()
        {
            IsDisposed = true;
            _refresh = null;
        }
    }

    private sealed class NonDisposableServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class SourceOwnedServiceProvider(
        VaultClient client,
        SecretRefresher refresher) : IServiceProvider, IDisposable
    {
        public object? GetService(Type serviceType) => serviceType switch
        {
            var type when type == typeof(VaultClient) => client,
            var type when type == typeof(SecretRefresher) => refresher,
            var type when type == typeof(ILogger<VaultConfigurationProvider>) =>
                NullLogger<VaultConfigurationProvider>.Instance,
            _ => null
        };

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            refresher.Dispose();
        }
    }
}
