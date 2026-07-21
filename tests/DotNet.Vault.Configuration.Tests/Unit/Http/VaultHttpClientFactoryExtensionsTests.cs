using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Http;

public class VaultHttpClientFactoryExtensionsTests
{
    [Fact]
    public void AddVaultHttpClient_RegistersNamedClient()
    {
        var services = new ServiceCollection();
        var options = new VaultOptions { Uri = new Uri("http://localhost:8200") };

        services.AddVaultHttpClient(options);

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName);
        Assert.NotNull(client);
        Assert.Equal(new Uri("http://localhost:8200"), client.BaseAddress);
    }

    [Fact]
    public void AddVaultHttpClient_AppliesTimeout()
    {
        var services = new ServiceCollection();
        var options = new VaultOptions
        {
            Uri = new Uri("http://localhost:8200"),
            Timeout = TimeSpan.FromSeconds(45)
        };

        services.AddVaultHttpClient(options);
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(VaultHttpClientFactoryExtensions.VaultClientName);

        Assert.Equal(TimeSpan.FromSeconds(45), client.Timeout);
    }

    [Fact]
    public void VaultClientName_IsVaultClient()
    {
        Assert.Equal("vault-client", VaultHttpClientFactoryExtensions.VaultClientName);
    }
}
