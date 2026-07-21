using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Http;

public class VaultAuthDelegatingHandlerTests
{
    [Fact]
    public async Task SendAsync_AttachesTokenHeader_WhenNotPresent()
    {
        // Arrange
        var mockAuth = new Mock<IVaultAuthenticationProvider>();
        mockAuth.Setup(x => x.GetTokenAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("test-token");

        var handler = new VaultAuthDelegatingHandler(mockAuth.Object, Mock.Of<ILogger<VaultAuthDelegatingHandler>>());
        var innerHandler = new Mock<HttpMessageHandler>();
        innerHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
        handler.InnerHandler = innerHandler.Object;

        var client = new HttpClient(handler);

        // Act
        await client.GetAsync("http://localhost/v1/test");

        // Assert
        innerHandler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r => r.Headers.GetValues("X-Vault-Token").First() == "test-token"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_DoesNotOverrideExistingToken()
    {
        var mockAuth = new Mock<IVaultAuthenticationProvider>();
        mockAuth.Setup(x => x.GetTokenAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("new-token");

        var handler = new VaultAuthDelegatingHandler(mockAuth.Object, Mock.Of<ILogger<VaultAuthDelegatingHandler>>());
        var innerHandler = new Mock<HttpMessageHandler>();
        innerHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
        handler.InnerHandler = innerHandler.Object;

        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/v1/test");
        request.Headers.Add("X-Vault-Token", "user-set-token");

        await client.SendAsync(request);

        innerHandler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r => r.Headers.GetValues("X-Vault-Token").First() == "user-set-token"),
            ItExpr.IsAny<CancellationToken>());
        mockAuth.Verify(x => x.GetTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_ContinuesWithoutToken_WhenAuthFails()
    {
        var mockAuth = new Mock<IVaultAuthenticationProvider>();
        mockAuth.Setup(x => x.GetTokenAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("auth fail"));

        var handler = new VaultAuthDelegatingHandler(mockAuth.Object, Mock.Of<ILogger<VaultAuthDelegatingHandler>>());
        var innerHandler = new Mock<HttpMessageHandler>();
        innerHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
        handler.InnerHandler = innerHandler.Object;

        var client = new HttpClient(handler);

        // Act - should not throw
        await client.GetAsync("http://localhost/v1/test");

        // Assert - request sent without token
        innerHandler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
