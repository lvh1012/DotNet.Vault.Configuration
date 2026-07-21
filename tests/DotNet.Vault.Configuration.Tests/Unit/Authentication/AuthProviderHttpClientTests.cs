using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Net.Http;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Authentication;

public class AuthProviderHttpClientTests
{
    [Fact]
    public async Task AppRoleAuthProvider_UsesUnauthenticatedClient_AndParsesToken()
    {
        // Arrange
        var handler = new FakeLoginHandler();
        var factoryMock = CreateFactoryMock(handler);

        var provider = new AppRoleAuthProvider(
            Options.Create(new AppRoleAuthenticationOptions
            {
                RoleId = "role-id",
                SecretId = "secret-id",
                AppRolePath = "approle"
            }),
            factoryMock.Object,
            Mock.Of<ILogger<AppRoleAuthProvider>>());

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.Equal("login-token", token);
        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(handler.LastRequest);
        Assert.EndsWith("/v1/auth/approle/login", handler.LastRequest!.RequestUri!.ToString());
        factoryMock.Verify(
            x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultAuthClientName),
            Times.Once);
        factoryMock.Verify(
            x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName),
            Times.Never);
    }

    [Fact]
    public async Task KubernetesAuthProvider_UsesUnauthenticatedClient_AndParsesToken()
    {
        // Arrange
        var jwtPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(jwtPath, "service-account-jwt");

        try
        {
            var handler = new FakeLoginHandler();
            var factoryMock = CreateFactoryMock(handler);

            var provider = new KubernetesAuthProvider(
                Options.Create(new KubernetesAuthenticationOptions
                {
                    Role = "my-role",
                    KubernetesRolePath = "kubernetes",
                    ServiceAccountTokenPath = jwtPath
                }),
                factoryMock.Object,
                Mock.Of<ILogger<KubernetesAuthProvider>>());

            // Act
            var token = await provider.GetTokenAsync();

            // Assert
            Assert.Equal("login-token", token);
            Assert.Equal(1, handler.RequestCount);
            Assert.NotNull(handler.LastRequest);
            Assert.EndsWith("/v1/auth/kubernetes/login", handler.LastRequest!.RequestUri!.ToString());
            factoryMock.Verify(
                x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultAuthClientName),
                Times.Once);
            factoryMock.Verify(
                x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName),
                Times.Never);
        }
        finally
        {
            File.Delete(jwtPath);
        }
    }

    [Fact]
    public async Task LdapAuthProvider_UsesUnauthenticatedClient_AndParsesToken()
    {
        // Arrange
        var handler = new FakeLoginHandler();
        var factoryMock = CreateFactoryMock(handler);

        var provider = new LdapAuthProvider(
            Options.Create(new LdapAuthenticationOptions
            {
                Username = "user",
                Password = "pass",
                LdapPath = "ldap"
            }),
            factoryMock.Object,
            Mock.Of<ILogger<LdapAuthProvider>>());

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.Equal("login-token", token);
        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(handler.LastRequest);
        Assert.EndsWith("/v1/auth/ldap/login/user", handler.LastRequest!.RequestUri!.ToString());
        factoryMock.Verify(
            x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultAuthClientName),
            Times.Once);
        factoryMock.Verify(
            x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName),
            Times.Never);
    }

    [Fact]
    public async Task TokenAuthProvider_StaticToken_IsAttachedToAuthenticatedRequest()
    {
        // Arrange
        var tokenProvider = new TokenAuthProvider(
            Options.Create(new TokenAuthenticationOptions { Token = "static-token" }));

        var authHandler = new VaultAuthDelegatingHandler(
            tokenProvider,
            Mock.Of<ILogger<VaultAuthDelegatingHandler>>());

        var innerHandler = new FakeInnerHandler();
        authHandler.InnerHandler = innerHandler;

        var client = new HttpClient(authHandler);

        // Act
        await client.GetAsync("http://localhost:8200/v1/test");

        // Assert
        Assert.NotNull(innerHandler.LastRequest);
        Assert.True(innerHandler.LastRequest!.Headers.Contains("X-Vault-Token"));
        Assert.Equal(
            "static-token",
            innerHandler.LastRequest.Headers.GetValues("X-Vault-Token").Single());
    }

    private static Mock<IHttpClientFactory> CreateFactoryMock(HttpMessageHandler handler)
    {
        var mock = new Mock<IHttpClientFactory>();
        var baseAddress = new Uri("http://localhost:8200");

        mock.Setup(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultAuthClientName))
            .Returns(() => new HttpClient(handler) { BaseAddress = baseAddress });

        mock.Setup(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName))
            .Throws(new InvalidOperationException(
                "The authenticated vault client must not be used for login requests."));

        return mock;
    }

    private sealed class FakeLoginHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"auth":{"client_token":"login-token","lease_duration":3600}}""")
            };

            return Task.FromResult(response);
        }
    }

    private sealed class FakeInnerHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
