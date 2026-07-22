using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DotNet.Vault.Configuration.Security;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
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
    public void AddVaultHttpClient_DefaultSslOptions_UsesSecurePrimaryHandlerConfiguration()
    {
        var services = new ServiceCollection();
        var options = new VaultOptions { Uri = new Uri("https://vault.example.test:8200") };

        services.AddVaultHttpClient(options);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var primaryHandler = GetConfiguredPrimaryHandler(
            scope.ServiceProvider,
            VaultHttpClientFactoryExtensions.VaultClientName);
        var handler = GetSocketsPrimaryHandler(primaryHandler);

        Assert.Equal(SslProtocols.Tls12, handler.SslOptions.EnabledSslProtocols);
        Assert.Equal(X509RevocationMode.Online, handler.SslOptions.CertificateRevocationCheckMode);
        Assert.Equal("vault.example.test", handler.SslOptions.TargetHost);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.Null(handler.SslOptions.CertificateChainPolicy);
        Assert.True(handler.SslOptions.ClientCertificates is null or { Count: 0 });
    }

    [Fact]
    public void AddVaultHttpClient_ConfiguredSslOptions_AppliesCertificatesAndValidationSettingsToBothClients()
    {
        const string clientCertificatePassword = "test-password";
        var certificateDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(certificateDirectory);

        try
        {
            using var caCertificate = CreateCertificate("CN=Test CA");
            using var clientCertificate = CreateCertificate("CN=Test Client");
            var caCertificatePath = Path.Combine(certificateDirectory, "ca.cer");
            var clientCertificatePath = Path.Combine(certificateDirectory, "client.pfx");
            File.WriteAllBytes(caCertificatePath, caCertificate.Export(X509ContentType.Cert));
            File.WriteAllBytes(clientCertificatePath, clientCertificate.Export(X509ContentType.Pfx, clientCertificatePassword));

            var services = new ServiceCollection();
            var options = new VaultOptions
            {
                Uri = new Uri("https://vault.example.test:8200"),
                Ssl = new VaultSslOptions
                {
                    CaCertificatePath = caCertificatePath,
                    ClientCertificatePath = clientCertificatePath,
                    ClientCertificatePassword = clientCertificatePassword,
                    SkipVerify = true,
                    Protocol = SslProtocols.Tls13,
                    CheckCertificateRevocation = false,
                    ServerName = "vault.internal"
                }
            };

            services.AddVaultHttpClient(options);

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            using var vaultPrimaryHandler = GetConfiguredPrimaryHandler(
                scope.ServiceProvider,
                VaultHttpClientFactoryExtensions.VaultClientName);
            AssertSslConfiguration(
                GetSocketsPrimaryHandler(vaultPrimaryHandler),
                caCertificate,
                clientCertificate);

            using var authPrimaryHandler = GetConfiguredPrimaryHandler(
                scope.ServiceProvider,
                VaultHttpClientFactoryExtensions.VaultAuthClientName);
            AssertSslConfiguration(
                GetSocketsPrimaryHandler(authPrimaryHandler),
                caCertificate,
                clientCertificate);
        }
        finally
        {
            Directory.Delete(certificateDirectory, recursive: true);
        }
    }

    [Fact]
    public void AddVaultHttpClient_CertificatePaths_DisposesLoadedCertificatesWithEachPrimaryHandler()
    {
        const string clientCertificatePassword = "test-password";
        var certificateDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(certificateDirectory);

        try
        {
            using var caCertificate = CreateCertificate("CN=Test CA");
            using var clientCertificate = CreateCertificate("CN=Test Client");
            var caCertificatePath = Path.Combine(certificateDirectory, "ca.cer");
            var clientCertificatePath = Path.Combine(certificateDirectory, "client.pfx");
            File.WriteAllBytes(caCertificatePath, caCertificate.Export(X509ContentType.Cert));
            File.WriteAllBytes(clientCertificatePath, clientCertificate.Export(X509ContentType.Pfx, clientCertificatePassword));

            var services = new ServiceCollection();
            services.AddVaultHttpClient(new VaultOptions
            {
                Uri = new Uri("https://vault.example.test:8200"),
                Ssl = new VaultSslOptions
                {
                    CaCertificatePath = caCertificatePath,
                    ClientCertificatePath = clientCertificatePath,
                    ClientCertificatePassword = clientCertificatePassword
                }
            });

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var vaultPrimaryHandler = GetConfiguredPrimaryHandler(
                scope.ServiceProvider,
                VaultHttpClientFactoryExtensions.VaultClientName);
            var authPrimaryHandler = GetConfiguredPrimaryHandler(
                scope.ServiceProvider,
                VaultHttpClientFactoryExtensions.VaultAuthClientName);
            var vaultSslOptions = GetSocketsPrimaryHandler(vaultPrimaryHandler).SslOptions;
            var authSslOptions = GetSocketsPrimaryHandler(authPrimaryHandler).SslOptions;
            var loadedCertificates = new[]
            {
                Assert.IsType<X509Certificate2>(vaultSslOptions.ClientCertificates!.Cast<X509Certificate2>().Single()),
                Assert.IsType<X509Certificate2>(vaultSslOptions.CertificateChainPolicy!.CustomTrustStore.Cast<X509Certificate2>().Single()),
                Assert.IsType<X509Certificate2>(authSslOptions.ClientCertificates!.Cast<X509Certificate2>().Single()),
                Assert.IsType<X509Certificate2>(authSslOptions.CertificateChainPolicy!.CustomTrustStore.Cast<X509Certificate2>().Single())
            };

            vaultPrimaryHandler.Dispose();
            authPrimaryHandler.Dispose();

            foreach (var certificate in loadedCertificates)
            {
                Assert.Throws<CryptographicException>(() => certificate.Export(X509ContentType.Cert));
            }
        }
        finally
        {
            Directory.Delete(certificateDirectory, recursive: true);
        }
    }

    [Fact]
    public void AddVaultHttpClient_SuppliedCertificates_RemainUsableAfterPrimaryHandlerDisposal()
    {
        using var caCertificate = CreateCertificate("CN=Test CA");
        using var clientCertificate = CreateCertificate("CN=Test Client");
        var services = new ServiceCollection();
        services.AddVaultHttpClient(new VaultOptions
        {
            Uri = new Uri("https://vault.example.test:8200"),
            Ssl = new VaultSslOptions
            {
                CaCertificate = caCertificate,
                ClientCertificate = clientCertificate
            }
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var vaultPrimaryHandler = GetConfiguredPrimaryHandler(
            scope.ServiceProvider,
            VaultHttpClientFactoryExtensions.VaultClientName);
        using var authPrimaryHandler = GetConfiguredPrimaryHandler(
            scope.ServiceProvider,
            VaultHttpClientFactoryExtensions.VaultAuthClientName);

        vaultPrimaryHandler.Dispose();
        authPrimaryHandler.Dispose();

        Assert.NotEmpty(caCertificate.Export(X509ContentType.Cert));
        Assert.NotEmpty(clientCertificate.Export(X509ContentType.Cert));
    }

    private static HttpMessageHandler GetConfiguredPrimaryHandler(IServiceProvider services, string clientName)
    {
        var builder = services.GetRequiredService<HttpMessageHandlerBuilder>();
        builder.Name = clientName;
        var options = services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(clientName);

        foreach (var action in options.HttpMessageHandlerBuilderActions)
        {
            action(builder);
        }

        return builder.PrimaryHandler;
    }

    private static SocketsHttpHandler GetSocketsPrimaryHandler(HttpMessageHandler primaryHandler)
    {
        return primaryHandler switch
        {
            SocketsHttpHandler handler => handler,
            DelegatingHandler { InnerHandler: SocketsHttpHandler handler } => handler,
            _ => throw new Xunit.Sdk.XunitException("Expected a SocketsHttpHandler primary handler.")
        };
    }

    private static void AssertSslConfiguration(
        SocketsHttpHandler handler,
        X509Certificate2 caCertificate,
        X509Certificate2 clientCertificate)
    {
        var sslOptions = handler.SslOptions;
        Assert.Equal(SslProtocols.Tls13, sslOptions.EnabledSslProtocols);
        Assert.Equal(X509RevocationMode.NoCheck, sslOptions.CertificateRevocationCheckMode);
        Assert.Equal("vault.internal", sslOptions.TargetHost);
        Assert.NotNull(sslOptions.RemoteCertificateValidationCallback);
        Assert.True(sslOptions.RemoteCertificateValidationCallback!(null!, null!, null!, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.NotNull(sslOptions.CertificateChainPolicy);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, sslOptions.CertificateChainPolicy!.TrustMode);
        Assert.Contains(sslOptions.CertificateChainPolicy.CustomTrustStore.Cast<X509Certificate2>(), certificate => certificate.Thumbprint == caCertificate.Thumbprint);
        Assert.NotNull(sslOptions.ClientCertificates);
        Assert.Contains(sslOptions.ClientCertificates!.Cast<X509Certificate2>(), certificate => certificate.Thumbprint == clientCertificate.Thumbprint);
    }

    private static X509Certificate2 CreateCertificate(string subjectName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            subjectName,
            key,
            HashAlgorithmName.SHA256);

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), password: null);
    }

    [Fact]
    public void VaultClientName_IsVaultClient()
    {
        Assert.Equal("vault-client", VaultHttpClientFactoryExtensions.VaultClientName);
    }
}
