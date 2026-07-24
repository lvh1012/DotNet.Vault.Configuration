using System.Net;
using System.Text;
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit;

public sealed class PublicConstructorCompatibilityTests
{
    [Fact]
    public async Task VaultClient_HttpClientConstructor_UsesProvidedClientAndPreservesClientConfiguration()
    {
        using var httpClient = CreateClient("{\"initialized\":true,\"sealed\":false,\"standby\":false}");
        var options = new VaultOptions
        {
            Uri = new Uri("https://vault.compatibility.test"),
            Timeout = TimeSpan.FromSeconds(42)
        };

        var client = new VaultClient(httpClient, options, [], [], NullLogger<VaultClient>.Instance);

        var health = await client.GetHealthAsync();

        Assert.True(health.Initialized);
        Assert.Equal(options.Uri, httpClient.BaseAddress);
        Assert.Equal(options.Timeout, httpClient.Timeout);
    }

    [Fact]
    public async Task HttpClientAuthenticationConstructors_UseProvidedClientWithoutDisposingIt()
    {
        var kubernetesTokenPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(kubernetesTokenPath, "service-account-token");

        try
        {
            using var appRoleClient = CreateClient(LoginResponse);
            using var kubernetesClient = CreateClient(LoginResponse);
            using var ldapClient = CreateClient(LoginResponse);

            using var appRole = new AppRoleAuthProvider(
                Options.Create(new AppRoleAuthenticationOptions { RoleId = "role", SecretId = "secret" }),
                appRoleClient);
            using var kubernetes = new KubernetesAuthProvider(
                Options.Create(new KubernetesAuthenticationOptions { Role = "role", ServiceAccountTokenPath = kubernetesTokenPath }),
                kubernetesClient);
            using var ldap = new LdapAuthProvider(
                Options.Create(new LdapAuthenticationOptions { Username = "user", Password = "password" }),
                ldapClient);

            Assert.Equal("compatibility-token", await appRole.GetTokenAsync());
            Assert.Equal("compatibility-token", await kubernetes.GetTokenAsync());
            Assert.Equal("compatibility-token", await ldap.GetTokenAsync());

            Assert.Equal(HttpStatusCode.OK, (await appRoleClient.GetAsync("/still-usable")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await kubernetesClient.GetAsync("/still-usable")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ldapClient.GetAsync("/still-usable")).StatusCode);
        }
        finally
        {
            File.Delete(kubernetesTokenPath);
        }
    }

    [Fact]
    public async Task HttpClientBackendConstructors_UseProvidedClientsWithoutARefresher()
    {
        using var kvClient = CreateClient("{\"data\":{\"key\":\"value\"}}");
        using var databaseClient = CreateClient("{\"data\":{\"username\":\"user\"}}");
        using var pkiClient = CreateClient("{\"data\":{\"certificate\":\"certificate\",\"private_key\":\"key\",\"ca_chain\":\"chain\"}}");

        var kv = new KvSecretBackend(new KvSecretBackendOptions { BackendPath = "secret", Version = 1 }, kvClient);
        var database = new DatabaseSecretBackend(new DatabaseSecretBackendOptions { BackendPath = "database" }, databaseClient);
        var pki = new PkiSecretBackend(new PkiSecretBackendOptions { BackendPath = "pki", CommonName = "example.test" }, pkiClient);

        var kvResult = await kv.GetSecretsAsync(new SecretRequest { Path = "secret/application" });
        var databaseResult = await database.GetSecretsAsync(new SecretRequest { Path = "database/creds/role" });
        var pkiResult = await pki.GetSecretsAsync(new SecretRequest { Path = "pki/issue/role" });

        Assert.Equal("value", kvResult.Secrets["key"]);
        Assert.Equal("user", databaseResult.Secrets["username"]);
        Assert.Equal("certificate", pkiResult.Secrets["certificate.pem"]);
    }

    [Fact]
    public async Task SecretRefresher_LegacyConstructor_CanStartAndStopWhenRefreshIsDisabled()
    {
        using var httpClient = CreateClient("{\"initialized\":true,\"sealed\":false,\"standby\":false}");
        var options = new VaultOptions { Refresh = new VaultRefreshOptions { Enabled = false } };
        var client = new VaultClient(httpClient, options, [], [], NullLogger<VaultClient>.Instance);
        using var refresher = new SecretRefresher(client, options, NullLogger<SecretRefresher>.Instance);

        await refresher.StartAsync(CancellationToken.None);
        await refresher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void VaultConfigurationProvider_LegacyConstructor_IsAvailable()
    {
        using var httpClient = CreateClient("{\"initialized\":true,\"sealed\":false,\"standby\":false}");
        var options = new VaultOptions { Refresh = new VaultRefreshOptions { Enabled = false } };
        var client = new VaultClient(httpClient, options, [], [], NullLogger<VaultClient>.Instance);
        using var refresher = new SecretRefresher(client, options, NullLogger<SecretRefresher>.Instance);

        using var provider = new VaultConfigurationProvider(
            client,
            options,
            refresher,
            NullLogger<VaultConfigurationProvider>.Instance);
        Assert.Contains(
            typeof(VaultConfigurationProvider).GetConstructors(),
            constructor => constructor.GetParameters().Length == 4);
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task SecretRefresher_LegacyConstructor_RenewsLeasesWithTheVaultClientToken()
    {
        string? renewalToken = null;
        var renewalObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var httpClient = new HttpClient(new RoutingHandler(request =>
        {
            renewalToken = request.Headers.TryGetValues("X-Vault-Token", out var tokens)
                ? tokens.Single()
                : null;
            renewalObserved.TrySetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"lease_duration\":60}", Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://vault.compatibility.test")
        };
        var options = new VaultOptions
        {
            Authentication = new VaultAuthenticationConfiguration { Method = "token" },
            Refresh = new VaultRefreshOptions { Enabled = true, Interval = TimeSpan.FromMilliseconds(10) }
        };
        var tokenProvider = new TokenAuthProvider(Options.Create(new TokenAuthenticationOptions { Token = "legacy-token" }));
        var client = new VaultClient(httpClient, options, [tokenProvider], [], NullLogger<VaultClient>.Instance);
        using var refresher = new SecretRefresher(client, options, NullLogger<SecretRefresher>.Instance);

        refresher.TrackSecret(
            "database/creds/application",
            new SecretResult
            {
                LeaseId = "database/creds/application/lease",
                LeaseDuration = TimeSpan.FromMinutes(1),
                ExpireTime = DateTimeOffset.UtcNow.AddMinutes(-1),
                Renewable = true
            });

        await refresher.StartAsync(CancellationToken.None);
        await renewalObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await refresher.StopAsync(CancellationToken.None);

        Assert.Equal("legacy-token", renewalToken);
    }


    private const string LoginResponse = "{\"auth\":{\"client_token\":\"compatibility-token\",\"lease_duration\":3600}}";

    private static HttpClient CreateClient(string responseBody)
    {
        return new HttpClient(new CompatibilityHandler(responseBody))
        {
            BaseAddress = new Uri("https://vault.compatibility.test")
        };
    }

    private sealed class CompatibilityHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

}
