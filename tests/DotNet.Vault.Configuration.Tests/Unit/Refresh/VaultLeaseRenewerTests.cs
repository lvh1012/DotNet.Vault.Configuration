using DotNet.Vault.Configuration.Core.Exceptions;
using DotNet.Vault.Configuration.Http;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Refresh;

public class VaultLeaseRenewerTests
{
    private static IHttpClientFactory MockFactoryWithResponse(HttpStatusCode status, string body)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName))
            .Returns(new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost/") });
        return factory.Object;
    }

    [Fact]
    public async Task RenewAsync_Success_ReturnsNewDuration()
    {
        var factory = MockFactoryWithResponse(HttpStatusCode.OK, "{\"lease_id\":\"abc\",\"lease_duration\":3600}");
        var renewer = new VaultLeaseRenewer(factory, Mock.Of<ILogger<VaultLeaseRenewer>>());

        var result = await renewer.RenewAsync("abc", TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromHours(1), result);
    }

    [Fact]
    public async Task RenewAsync_Failure_ThrowsVaultLeaseRenewalException()
    {
        var factory = MockFactoryWithResponse(HttpStatusCode.Forbidden, "{\"errors\":[\"permission denied\"]}");
        var renewer = new VaultLeaseRenewer(factory, Mock.Of<ILogger<VaultLeaseRenewer>>());

        await Assert.ThrowsAsync<VaultLeaseRenewalException>(() => renewer.RenewAsync("abc", TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task RenewAsync_NoLeaseDuration_ReturnsNull()
    {
        var factory = MockFactoryWithResponse(HttpStatusCode.OK, "{\"lease_id\":\"abc\"}");
        var renewer = new VaultLeaseRenewer(factory, Mock.Of<ILogger<VaultLeaseRenewer>>());

        var result = await renewer.RenewAsync("abc", TimeSpan.FromHours(1));

        Assert.Null(result);
    }
}
