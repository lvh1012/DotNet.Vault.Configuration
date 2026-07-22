using System.Net;
using System.Net.Http;
using System.Text;
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Exceptions;
using DotNet.Vault.Configuration.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Core;

public class VaultClientTests
{
    [Fact]
    public async Task LoadSecretsAsync_UsesMatchingBackendAndReturnsItsSecrets()
    {
        var backend = new Mock<IVaultSecretBackend>();
        backend.Setup(x => x.CanHandle("kv/application")).Returns(true);
        backend.Setup(x => x.GetSecretsAsync(It.IsAny<SecretRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretResult { Secrets = new Dictionary<string, string> { ["setting"] = "value" } });
        var client = CreateClient(backends: [backend.Object]);

        var secrets = await client.LoadSecretsAsync(["kv/application"]);

        Assert.Equal("value", secrets["setting"]);
        backend.Verify(
            x => x.GetSecretsAsync(
                It.Is<SecretRequest>(request => request.Path == "kv/application"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoadSecretsAsync_UsesFirstBackendThatHandlesPath()
    {
        var firstBackend = new Mock<IVaultSecretBackend>();
        firstBackend.Setup(x => x.CanHandle("kv/application")).Returns(true);
        firstBackend.Setup(x => x.GetSecretsAsync(It.IsAny<SecretRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretResult { Secrets = new Dictionary<string, string> { ["source"] = "first" } });
        var secondBackend = new Mock<IVaultSecretBackend>();
        secondBackend.Setup(x => x.CanHandle("kv/application")).Returns(true);
        var client = CreateClient(backends: [firstBackend.Object, secondBackend.Object]);

        var secrets = await client.LoadSecretsAsync(["kv/application"]);

        Assert.Equal("first", secrets["source"]);
        secondBackend.Verify(x => x.GetSecretsAsync(It.IsAny<SecretRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoadSecretsAsync_LaterPathsOverwriteDuplicateSecretKeys()
    {
        var backend = new Mock<IVaultSecretBackend>();
        backend.Setup(x => x.CanHandle(It.IsAny<string>())).Returns(true);
        backend.SetupSequence(x => x.GetSecretsAsync(It.IsAny<SecretRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretResult { Secrets = new Dictionary<string, string> { ["setting"] = "first", ["one"] = "1" } })
            .ReturnsAsync(new SecretResult { Secrets = new Dictionary<string, string> { ["setting"] = "second", ["two"] = "2" } });
        var client = CreateClient(backends: [backend.Object]);

        var secrets = await client.LoadSecretsAsync(["kv/first", "kv/second"]);

        Assert.Equal("second", secrets["setting"]);
        Assert.Equal("1", secrets["one"]);
        Assert.Equal("2", secrets["two"]);
    }

    [Fact]
    public async Task LoadSecretsAsync_WhenNoBackendHandlesPath_ThrowsExceptionWithPath()
    {
        var client = CreateClient();

        var exception = await Assert.ThrowsAsync<VaultBackendNotSupportedException>(
            () => client.LoadSecretsAsync(["unsupported/path"]));

        Assert.Equal("unsupported/path", exception.BackendType);
    }

    [Fact]
    public async Task GetTokenAsync_UsesProviderMatchingConfiguredMethodAndCancellationToken()
    {
        using var cancellationSource = new CancellationTokenSource();
        var provider = new Mock<IVaultAuthenticationProvider>();
        provider.SetupGet(x => x.AuthenticationMethod).Returns("approle");
        provider.Setup(x => x.GetTokenAsync(cancellationSource.Token)).ReturnsAsync("vault-token");
        var client = CreateClient(
            options: new VaultOptions { Authentication = new VaultAuthenticationConfiguration { Method = "approle" } },
            authProviders: [provider.Object]);

        var token = await client.GetTokenAsync(cancellationSource.Token);

        Assert.Equal("vault-token", token);
        provider.Verify(x => x.GetTokenAsync(cancellationSource.Token), Times.Once);
    }

    [Fact]
    public async Task GetTokenAsync_WhenNoProviderMatches_ThrowsExceptionWithConfiguredMethod()
    {
        var client = CreateClient(
            options: new VaultOptions { Authentication = new VaultAuthenticationConfiguration { Method = "kubernetes" } });

        var exception = await Assert.ThrowsAsync<VaultAuthenticationException>(() => client.GetTokenAsync());

        Assert.Equal("kubernetes", exception.AuthenticationMethod);
    }

    [Fact]
    public async Task GetHealthAsync_UsesNamedClientHealthPathAndDeserializesResponse()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"initialized\":true,\"sealed\":false,\"standby\":true,\"version\":\"1.17.0\"}", Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName)).Returns(httpClient);
        var client = CreateClient(httpClientFactory: factory.Object);

        var health = await client.GetHealthAsync();

        Assert.True(health.Initialized);
        Assert.False(health.Sealed);
        Assert.True(health.Standby);
        Assert.Equal("1.17.0", health.Version);
        factory.Verify(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName), Times.Once);
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(request => request.Method == HttpMethod.Get && request.RequestUri!.PathAndQuery == "/v1/sys/health"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetHealthAsync_WhenRequestFails_WrapsFailureWithConfiguredVaultUri()
    {
        var networkFailure = new HttpRequestException("connection failed");
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(networkFailure);
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName)).Returns(httpClient);
        var options = new VaultOptions { Uri = new Uri("https://configured-vault.test") };
        var client = CreateClient(httpClientFactory: factory.Object, options: options);

        var exception = await Assert.ThrowsAsync<VaultConnectionException>(() => client.GetHealthAsync());

        Assert.Equal(options.Uri, exception.VaultUri);
        Assert.Same(networkFailure, exception.InnerException);
    }

    [Fact]
    public async Task IsAuthenticationValidAsync_SendsTokenToLookupSelfEndpoint()
    {
        var provider = new Mock<IVaultAuthenticationProvider>();
        provider.SetupGet(x => x.AuthenticationMethod).Returns("token");
        provider.Setup(x => x.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("vault-token");
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName)).Returns(httpClient);
        var client = CreateClient(httpClientFactory: factory.Object, authProviders: [provider.Object]);

        var isValid = await client.IsAuthenticationValidAsync();

        Assert.True(isValid);
        factory.Verify(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName), Times.Once);
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(request =>
                request.Method == HttpMethod.Get &&
                request.RequestUri!.PathAndQuery == "/v1/auth/token/lookup-self" &&
                request.Headers.GetValues("X-Vault-Token").Single() == "vault-token"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task IsAuthenticationValidAsync_WhenVaultRejectsToken_ReturnsFalse()
    {
        var provider = new Mock<IVaultAuthenticationProvider>();
        provider.SetupGet(x => x.AuthenticationMethod).Returns("token");
        provider.Setup(x => x.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("vault-token");
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://vault.test") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName)).Returns(httpClient);
        var client = CreateClient(httpClientFactory: factory.Object, authProviders: [provider.Object]);

        var isValid = await client.IsAuthenticationValidAsync();

        Assert.False(isValid);
    }

    private static VaultClient CreateClient(
        IHttpClientFactory? httpClientFactory = null,
        VaultOptions? options = null,
        IEnumerable<IVaultAuthenticationProvider>? authProviders = null,
        IEnumerable<IVaultSecretBackend>? backends = null)
    {
        return new VaultClient(
            httpClientFactory ?? Mock.Of<IHttpClientFactory>(),
            options ?? new VaultOptions(),
            authProviders ?? [],
            backends ?? [],
            Mock.Of<ILogger<VaultClient>>());
    }
}
