using System.Reflection;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Core;

public class VaultConfigurationProviderTests
{
    [Fact]
    public async Task RefreshCycle_ReloadsProviderData_RaisesReload_AndStopsAfterDispose()
    {
        var options = new VaultOptions
        {
            Kv = new KvSecretBackendOptions { Enabled = true }
        };
        var backend = new Mock<IVaultSecretBackend>();
        backend.Setup(backend => backend.CanHandle(It.IsAny<string>())).Returns(true);
        backend.SetupSequence(backend => backend.GetSecretsAsync(It.IsAny<SecretRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretResult { Secrets = new Dictionary<string, string> { ["setting"] = "initial" } })
            .ReturnsAsync(new SecretResult { Secrets = new Dictionary<string, string> { ["setting"] = "refreshed" } });

        using var refresher = new SecretRefresher(
            options,
            Mock.Of<ILogger<SecretRefresher>>(),
            new NoopRefreshScheduler(),
            new VaultLeaseRenewer(
                Mock.Of<IHttpClientFactory>(),
                Mock.Of<ILogger<VaultLeaseRenewer>>()));
        using var provider = new VaultConfigurationProvider(
            new VaultClient(
                Mock.Of<IHttpClientFactory>(),
                options,
                [],
                [backend.Object],
                Mock.Of<ILogger<VaultClient>>()),
            options,
            refresher,
            Mock.Of<ILogger<VaultConfigurationProvider>>());
        var reloadCount = 0;

        provider.Load();
        using var registration = provider.GetReloadToken().RegisterChangeCallback(_ => reloadCount++, null);
        refresher.TrackSecret(
            "secret/data/application",
            new SecretResult
            {
                LeaseDuration = TimeSpan.FromMinutes(1),
                ExpireTime = DateTimeOffset.UtcNow.AddMinutes(-1)
            });

        await RunRefreshLoopAsync(refresher);

        Assert.True(provider.TryGet("setting", out var value));
        Assert.Equal("refreshed", value);
        Assert.Equal(1, reloadCount);

        provider.Dispose();
        await RunRefreshLoopAsync(refresher);

        backend.Verify(
            backend => backend.GetSecretsAsync(It.IsAny<SecretRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public void Load_WithConfiguredBackendPaths_MergesSecretsIntoConfiguration()
    {
        var options = new VaultOptions
        {
            Refresh = new VaultRefreshOptions { Enabled = false },
            Kv = new KvSecretBackendOptions
            {
                Enabled = true,
                BackendPath = "kv",
                ApplicationName = "orders",
                Profiles = ["development"]
            },
            Database = new DatabaseSecretBackendOptions { Enabled = true, Role = "app" },
            Pki = new PkiSecretBackendOptions { Enabled = true, Role = "web" }
        };
        var requestedPaths = new List<string>();
        var backend = new Mock<IVaultSecretBackend>(MockBehavior.Strict);
        backend.Setup(backend => backend.CanHandle(It.IsAny<string>())).Returns(true);
        backend
            .Setup(backend => backend.GetSecretsAsync(It.IsAny<SecretRequest>(), It.IsAny<CancellationToken>()))
            .Returns((SecretRequest request, CancellationToken _) =>
            {
                requestedPaths.Add(request.Path);
                return Task.FromResult(new SecretResult
                {
                    Secrets = request.Path switch
                    {
                        "kv/data/application" => new Dictionary<string, string>
                        {
                            ["shared"] = "base",
                            ["default"] = "loaded"
                        },
                        "kv/data/application/development" => new Dictionary<string, string>
                        {
                            ["shared"] = "profile"
                        },
                        "kv/data/orders" => new Dictionary<string, string>
                        {
                            ["application"] = "loaded"
                        },
                        "kv/data/orders/development" => new Dictionary<string, string>
                        {
                            ["profile"] = "loaded"
                        },
                        "database/creds/app" => new Dictionary<string, string>
                        {
                            ["username"] = "vault-user"
                        },
                        "pki/issue/web" => new Dictionary<string, string>
                        {
                            ["certificate"] = "certificate-data"
                        },
                        _ => throw new InvalidOperationException($"Unexpected path: {request.Path}")
                    }
                });
            });

        using var refresher = CreateRefresher(options, new ManualRefreshScheduler());
        using var provider = CreateProvider(options, refresher, backend.Object);

        provider.Load();

        Assert.Equal(
            [
                "kv/data/application",
                "kv/data/application/development",
                "kv/data/orders",
                "kv/data/orders/development",
                "database/creds/app",
                "pki/issue/web"
            ],
            requestedPaths);
        Assert.True(provider.TryGet("shared", out var shared));
        Assert.Equal("profile", shared);
        Assert.True(provider.TryGet("username", out var username));
        Assert.Equal("vault-user", username);
        Assert.True(provider.TryGet("certificate", out var certificate));
        Assert.Equal("certificate-data", certificate);
    }

    [Fact]
    public void Load_WithNoEnabledBackends_ExposesEmptyConfiguration()
    {
        var options = new VaultOptions
        {
            Refresh = new VaultRefreshOptions { Enabled = false }
        };

        using var refresher = CreateRefresher(options, new ManualRefreshScheduler());
        using var provider = CreateProvider(options, refresher);

        provider.Load();

        Assert.False(provider.TryGet("missing", out _));
        Assert.Empty(provider.GetChildKeys([], null));
    }

    [Fact]
    public void Load_WhenFailFastIsEnabled_PropagatesBackendFailure()
    {
        var options = new VaultOptions
        {
            FailFast = true,
            Refresh = new VaultRefreshOptions { Enabled = false },
            Kv = new KvSecretBackendOptions { Enabled = true }
        };
        var backend = FailingBackend(new InvalidOperationException("Vault is unavailable"));

        using var refresher = CreateRefresher(options, new ManualRefreshScheduler());
        using var provider = CreateProvider(options, refresher, backend.Object);

        var exception = Assert.Throws<InvalidOperationException>(provider.Load);

        Assert.Equal("Vault is unavailable", exception.Message);
    }

    [Fact]
    public void Load_WhenFailFastIsDisabled_ContinuesWithEmptyConfiguration()
    {
        var options = new VaultOptions
        {
            FailFast = false,
            Refresh = new VaultRefreshOptions { Enabled = false },
            Kv = new KvSecretBackendOptions { Enabled = true }
        };
        var backend = FailingBackend(new InvalidOperationException("Vault is unavailable"));

        using var refresher = CreateRefresher(options, new ManualRefreshScheduler());
        using var provider = CreateProvider(options, refresher, backend.Object);

        provider.Load();

        Assert.False(provider.TryGet("any-key", out _));
        Assert.Empty(provider.GetChildKeys([], null));
    }

    [Fact]
    public async Task RefreshCycle_ReplacesConfigurationAndRaisesReload()
    {
        var options = RefreshingOptions();
        var scheduler = new ManualRefreshScheduler();
        var backend = new Mock<IVaultSecretBackend>();
        backend.Setup(backend => backend.CanHandle(It.IsAny<string>())).Returns(true);
        backend.SetupSequence(backend => backend.GetSecretsAsync(It.IsAny<SecretRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretResult { Secrets = new Dictionary<string, string> { ["setting"] = "initial" } })
            .ReturnsAsync(new SecretResult { Secrets = new Dictionary<string, string> { ["setting"] = "refreshed" } });

        using var refresher = CreateRefresher(options, scheduler);
        using var provider = CreateProvider(options, refresher, backend.Object);
        var reloadCount = 0;

        provider.Load();
        using var registration = provider.GetReloadToken().RegisterChangeCallback(_ => reloadCount++, null);
        refresher.TrackSecret(
            "secret/data/application",
            new SecretResult
            {
                LeaseDuration = TimeSpan.FromMinutes(1),
                ExpireTime = DateTimeOffset.UtcNow.AddMinutes(-1)
            });

        await scheduler.TriggerAsync();

        Assert.True(provider.TryGet("setting", out var value));
        Assert.Equal("refreshed", value);
        Assert.Equal(1, reloadCount);
    }

    [Fact]
    public async Task RefreshCycle_WhenReloadFails_PreservesLastKnownConfiguration()
    {
        var options = RefreshingOptions();
        var scheduler = new ManualRefreshScheduler();
        var backend = new Mock<IVaultSecretBackend>();
        backend.Setup(backend => backend.CanHandle(It.IsAny<string>())).Returns(true);
        backend.SetupSequence(backend => backend.GetSecretsAsync(It.IsAny<SecretRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretResult { Secrets = new Dictionary<string, string> { ["setting"] = "initial" } })
            .ThrowsAsync(new InvalidOperationException("Vault is unavailable"));

        using var refresher = CreateRefresher(options, scheduler);
        using var provider = CreateProvider(options, refresher, backend.Object);
        var reloadCount = 0;

        provider.Load();
        using var registration = provider.GetReloadToken().RegisterChangeCallback(_ => reloadCount++, null);
        refresher.TrackSecret(
            "secret/data/application",
            new SecretResult
            {
                LeaseDuration = TimeSpan.FromMinutes(1),
                ExpireTime = DateTimeOffset.UtcNow.AddMinutes(-1)
            });

        await scheduler.TriggerAsync();

        Assert.True(provider.TryGet("setting", out var value));
        Assert.Equal("initial", value);
        Assert.Equal(0, reloadCount);
    }

    [Fact]
    public void Dispose_ReleasesOwnedServiceProviderOnlyOnce()
    {
        var options = new VaultOptions
        {
            Refresh = new VaultRefreshOptions { Enabled = false }
        };
        var ownedServiceProvider = new DisposeCounter();

        using var refresher = CreateRefresher(options, new ManualRefreshScheduler());
        var provider = CreateProvider(options, refresher, serviceProvider: ownedServiceProvider);

        provider.Dispose();
        provider.Dispose();

        Assert.Equal(1, ownedServiceProvider.DisposeCount);
    }

    private static SecretRefresher CreateRefresher(
        VaultOptions options,
        ISecretRefreshScheduler scheduler)
    {
        return new SecretRefresher(
            options,
            Mock.Of<ILogger<SecretRefresher>>(),
            scheduler,
            new VaultLeaseRenewer(
                Mock.Of<IHttpClientFactory>(),
                Mock.Of<ILogger<VaultLeaseRenewer>>()));
    }

    private static VaultConfigurationProvider CreateProvider(
        VaultOptions options,
        SecretRefresher refresher,
        IVaultSecretBackend? backend = null,
        IDisposable? serviceProvider = null)
    {
        return new VaultConfigurationProvider(
            new VaultClient(
                Mock.Of<IHttpClientFactory>(),
                options,
                [],
                backend is null ? [] : [backend],
                Mock.Of<ILogger<VaultClient>>()),
            options,
            refresher,
            Mock.Of<ILogger<VaultConfigurationProvider>>(),
            serviceProvider);
    }

    private static Mock<IVaultSecretBackend> FailingBackend(Exception exception)
    {
        var backend = new Mock<IVaultSecretBackend>(MockBehavior.Strict);
        backend.Setup(backend => backend.CanHandle(It.IsAny<string>())).Returns(true);
        backend
            .Setup(backend => backend.GetSecretsAsync(It.IsAny<SecretRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        return backend;
    }

    private static VaultOptions RefreshingOptions()
    {
        return new VaultOptions
        {
            Refresh = new VaultRefreshOptions
            {
                Enabled = true,
                Interval = TimeSpan.FromSeconds(1)
            },
            Kv = new KvSecretBackendOptions { Enabled = true }
        };
    }

    private static async Task RunRefreshLoopAsync(SecretRefresher refresher)
    {
        var refreshLoop = typeof(SecretRefresher).GetMethod(
            "RefreshLoopAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(refreshLoop);
        await Assert.IsAssignableFrom<Task>(refreshLoop.Invoke(refresher, null));
    }

    private sealed class ManualRefreshScheduler : ISecretRefreshScheduler
    {
        private Func<Task>? _refresh;

        public void Start(TimeSpan interval, Func<Task> refresh)
        {
            _refresh = refresh;
        }

        public void Stop()
        {
        }

        public Task TriggerAsync()
        {
            return _refresh is null
                ? Task.CompletedTask
                : _refresh();
        }

        public void Dispose()
        {
        }
    }

    private sealed class DisposeCounter : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class NoopRefreshScheduler : ISecretRefreshScheduler
    {
        public void Start(TimeSpan interval, Func<Task> refresh)
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
