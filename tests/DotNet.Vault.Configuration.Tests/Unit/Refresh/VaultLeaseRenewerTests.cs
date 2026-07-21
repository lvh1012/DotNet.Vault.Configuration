using DotNet.Vault.Configuration.Core.Exceptions;
using DotNet.Vault.Configuration.Http;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Refresh;

public sealed class VaultLeaseRenewerTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    private IHttpClientFactory MockFactoryWithResponse(
        HttpStatusCode status,
        string body,
        out Mock<HttpMessageHandler> handler,
        out Mock<IHttpClientFactory> factoryMock,
        Action<HttpRequestMessage>? assertRequest = null)
    {
        handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => assertRequest?.Invoke(request))
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        factoryMock = new Mock<IHttpClientFactory>();
        var client = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost/") };
        _disposables.Add(client);
        factoryMock.Setup(f => f.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName))
            .Returns(client);
        return factoryMock.Object;
    }

    [Fact]
    public async Task RenewAsync_Success_ReturnsNewDuration()
    {
        var factory = MockFactoryWithResponse(
            HttpStatusCode.OK,
            "{\"lease_id\":\"abc\",\"lease_duration\":3600}",
            out _,
            out _,
            request =>
            {
                Assert.Equal("/v1/sys/leases/renew", request.RequestUri?.PathAndQuery);
                Assert.Equal(HttpMethod.Put, request.Method);
            });
        var renewer = new VaultLeaseRenewer(factory, Mock.Of<ILogger<VaultLeaseRenewer>>());

        var result = await renewer.RenewAsync("abc", TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromHours(1), result);
    }

    [Fact]
    public async Task RenewAsync_Success_SendsCorrectPayload()
    {
        string? capturedJson = null;
        var factory = MockFactoryWithResponse(
            HttpStatusCode.OK,
            "{\"lease_id\":\"abc\",\"lease_duration\":3600}",
            out _,
            out _,
            request =>
            {
                Assert.Equal("/v1/sys/leases/renew", request.RequestUri?.PathAndQuery);
                Assert.Equal(HttpMethod.Put, request.Method);
                capturedJson = request.Content!.ReadAsStringAsync().Result;
            });
        var renewer = new VaultLeaseRenewer(factory, Mock.Of<ILogger<VaultLeaseRenewer>>());

        await renewer.RenewAsync("abc", TimeSpan.FromHours(1));

        Assert.NotNull(capturedJson);
        using var document = JsonDocument.Parse(capturedJson);
        var root = document.RootElement;
        Assert.Equal("abc", root.GetProperty("lease_id").GetString());
        Assert.Equal(3600, root.GetProperty("increment").GetInt32());
    }

    [Fact]
    public async Task RenewAsync_Failure_ThrowsVaultLeaseRenewalException()
    {
        var factory = MockFactoryWithResponse(
            HttpStatusCode.Forbidden,
            "{\"errors\":[\"permission denied\"]}",
            out _,
            out _,
            request =>
            {
                Assert.Equal("/v1/sys/leases/renew", request.RequestUri?.PathAndQuery);
                Assert.Equal(HttpMethod.Put, request.Method);
            });
        var renewer = new VaultLeaseRenewer(factory, Mock.Of<ILogger<VaultLeaseRenewer>>());

        var ex = await Assert.ThrowsAsync<VaultLeaseRenewalException>(() => renewer.RenewAsync("abc", TimeSpan.FromHours(1)));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task RenewAsync_NoLeaseDuration_ReturnsNull()
    {
        var factory = MockFactoryWithResponse(HttpStatusCode.OK, "{\"lease_id\":\"abc\"}", out _, out _);
        var renewer = new VaultLeaseRenewer(factory, Mock.Of<ILogger<VaultLeaseRenewer>>());

        var result = await renewer.RenewAsync("abc", TimeSpan.FromHours(1));

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenewAsync_InvalidLeaseId_ThrowsArgumentException(string? leaseId)
    {
        var factory = Mock.Of<IHttpClientFactory>();
        var renewer = new VaultLeaseRenewer(factory, Mock.Of<ILogger<VaultLeaseRenewer>>());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => renewer.RenewAsync(leaseId!, TimeSpan.FromHours(1)));
        Assert.Equal("leaseId", ex.ParamName);
    }

    [Fact]
    public async Task RenewAsync_NegativeIncrement_ThrowsArgumentOutOfRangeException()
    {
        var factory = Mock.Of<IHttpClientFactory>();
        var renewer = new VaultLeaseRenewer(factory, Mock.Of<ILogger<VaultLeaseRenewer>>());

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => renewer.RenewAsync("abc", TimeSpan.FromSeconds(-1)));
        Assert.Equal("increment", ex.ParamName);
    }

    [Fact]
    public async Task RenewAsync_IncrementOverflow_ThrowsArgumentOutOfRangeException()
    {
        var factory = Mock.Of<IHttpClientFactory>();
        var renewer = new VaultLeaseRenewer(factory, Mock.Of<ILogger<VaultLeaseRenewer>>());

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => renewer.RenewAsync("abc", TimeSpan.FromSeconds(int.MaxValue + 1L)));
        Assert.Equal("increment", ex.ParamName);
    }
}
