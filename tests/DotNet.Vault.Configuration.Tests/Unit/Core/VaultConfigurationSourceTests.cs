using System.Reflection;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Extensions;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Core;

public class VaultConfigurationSourceTests
{
    [Fact]
    public void Build_RefreshEnabled_StartsSharedRefresher_AndDisposesItsTimer()
    {
        var configuration = new ConfigurationBuilder()
            .AddVault(options =>
            {
                options.Refresh.Enabled = true;
                options.Refresh.Interval = TimeSpan.FromHours(1);
            })
            .Build();
        var configurationLifetime = Assert.IsAssignableFrom<IDisposable>(configuration);

        var provider = Assert.IsType<VaultConfigurationProvider>(Assert.Single(configuration.Providers));
        var refresher = Assert.IsType<SecretRefresher>(GetPrivateField(provider, "_refresher"));

        Assert.IsType<Timer>(GetPrivateField(refresher, "_refreshTimer"));

        configurationLifetime.Dispose();

        Assert.Null(GetPrivateField(refresher, "_refreshTimer"));
    }

    [Fact]
    public void Load_FailFastFailure_DisposesSourceOwnedRefresherTimer()
    {
        var source = new VaultConfigurationSource
        {
            Options = new VaultOptions
            {
                Uri = new Uri("http://127.0.0.1:1"),
                FailFast = true,
                Kv = new KvSecretBackendOptions { Enabled = true },
                Refresh = new VaultRefreshOptions
                {
                    Enabled = true,
                    Interval = TimeSpan.FromHours(1),
                    Retry = new VaultRetryOptions { MaxRetries = 0 }
                }
            }
        };

        var provider = Assert.IsType<VaultConfigurationProvider>(source.Build(new ConfigurationBuilder()));
        var refresher = Assert.IsType<SecretRefresher>(GetPrivateField(provider, "_refresher"));

        Assert.Throws<HttpRequestException>(provider.Load);

        Assert.Null(GetPrivateField(refresher, "_refreshTimer"));
    }

    private static object? GetPrivateField(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        return field.GetValue(instance);
    }
}
