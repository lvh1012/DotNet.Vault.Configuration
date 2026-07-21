using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Authentication;

public class TokenAuthProviderTests
{
    [Fact]
    public async Task GetTokenAsync_WithStaticToken_ReturnsToken()
    {
        // Arrange
        var options = Options.Create(new TokenAuthenticationOptions
        {
            Token = "test-token"
        });
        var provider = new TokenAuthProvider(options);

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.Equal("test-token", token);
    }

    [Fact]
    public async Task GetTokenAsync_WithDynamicProvider_CallsProvider()
    {
        // Arrange
        var callCount = 0;
        var options = Options.Create(new TokenAuthenticationOptions
        {
            TokenProvider = () =>
            {
                callCount++;
                return Task.FromResult($"dynamic-token-{callCount}");
            }
        });
        var provider = new TokenAuthProvider(options);

        // Act
        var token1 = await provider.GetTokenAsync();
        var token2 = await provider.GetTokenAsync();

        // Assert
        Assert.Equal("dynamic-token-1", token1);
        Assert.Equal("dynamic-token-2", token2);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public void AuthenticationMethod_ReturnsToken()
    {
        // Arrange
        var options = Options.Create(new TokenAuthenticationOptions());
        var provider = new TokenAuthProvider(options);

        // Act & Assert
        Assert.Equal("token", provider.AuthenticationMethod);
    }
}
