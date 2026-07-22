using System.Security.Authentication;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Extensions;
using DotNet.Vault.Configuration.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Security;

public class VaultSslOptionsTests
{
    [Fact]
    public void Constructor_UsesSecureAndBackwardCompatibleDefaults()
    {
        var options = new VaultSslOptions();

        Assert.Null(options.CaCertificatePath);
        Assert.Null(options.ClientCertificatePath);
        Assert.Null(options.ClientCertificatePassword);
        Assert.Null(options.CaCertificate);
        Assert.Null(options.ClientCertificate);
        Assert.False(options.SkipVerify);
        Assert.Equal(SslProtocols.Tls12, options.Protocol);
        Assert.True(options.CheckCertificateRevocation);
        Assert.Null(options.ServerName);
    }

    [Fact]
    public void AddVault_WithSslConfiguration_BindsSslOptions()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Vault:Ssl:CaCertificatePath"] = "/certificates/ca.pem",
            ["Vault:Ssl:ClientCertificatePath"] = "/certificates/client.pfx",
            ["Vault:Ssl:ClientCertificatePassword"] = "client-password",
            ["Vault:Ssl:SkipVerify"] = "true",
            ["Vault:Ssl:Protocol"] = "Tls13",
            ["Vault:Ssl:CheckCertificateRevocation"] = "false",
            ["Vault:Ssl:ServerName"] = "vault.internal"
        };
        var vaultConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build()
            .GetSection("Vault");
        var builder = new ConfigurationBuilder().AddVault(vaultConfiguration);

        var source = Assert.IsType<VaultConfigurationSource>(Assert.Single(builder.Sources));

        Assert.Equal("/certificates/ca.pem", source.Options.Ssl.CaCertificatePath);
        Assert.Equal("/certificates/client.pfx", source.Options.Ssl.ClientCertificatePath);
        Assert.Equal("client-password", source.Options.Ssl.ClientCertificatePassword);
        Assert.True(source.Options.Ssl.SkipVerify);
        Assert.Equal(SslProtocols.Tls13, source.Options.Ssl.Protocol);
        Assert.False(source.Options.Ssl.CheckCertificateRevocation);
        Assert.Equal("vault.internal", source.Options.Ssl.ServerName);
    }

    [Fact]
    public void Constructor_InitializesAnIndependentSslOptionsInstance()
    {
        var first = new VaultOptions();
        var second = new VaultOptions();

        first.Ssl.SkipVerify = true;

        Assert.NotNull(first.Ssl);
        Assert.NotNull(second.Ssl);
        Assert.NotSame(first.Ssl, second.Ssl);
        Assert.True(first.Ssl.SkipVerify);
        Assert.False(second.Ssl.SkipVerify);
    }
}
