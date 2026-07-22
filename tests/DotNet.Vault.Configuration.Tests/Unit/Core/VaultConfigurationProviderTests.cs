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

        using var refresher = new SecretRefresher(options, Mock.Of<ILogger<SecretRefresher>>());
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

    private static async Task RunRefreshLoopAsync(SecretRefresher refresher)
    {
        var refreshLoop = typeof(SecretRefresher).GetMethod(
            "RefreshLoopAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(refreshLoop);
        await Assert.IsAssignableFrom<Task>(refreshLoop.Invoke(refresher, null));
    }
}
