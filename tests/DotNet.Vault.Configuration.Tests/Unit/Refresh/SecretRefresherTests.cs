using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Refresh;

public class SecretRefresherTests
{
    [Fact]
    public void Constructor_DoesNotRequireVaultClient()
    {
        var refresher = new SecretRefresher(
            new VaultOptions(),
            Mock.Of<ILogger<SecretRefresher>>());

        Assert.NotNull(refresher);
    }
}
