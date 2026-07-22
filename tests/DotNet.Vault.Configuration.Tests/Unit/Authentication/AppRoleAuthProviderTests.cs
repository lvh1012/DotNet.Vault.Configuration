using System.Net;
using System.Net.Http;
using System.Threading;
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Authentication;

public sealed class AppRoleAuthProviderTests
{
    [Fact]
    public async Task GetTokenAsync_ConcurrentCalls_OnlyOneLoginHappens()
    {
        var handler = new BlockingLoginHandler(leaseDuration: 3600);
        using var provider = CreateProvider(handler);

        var tokenTasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetTokenAsync())
            .ToArray();

        await handler.LoginStarted;
        handler.ReleaseLogin();

        var tokens = await Task.WhenAll(tokenTasks);

        Assert.All(tokens, token => Assert.Equal("cached-token", token));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetTokenAsync_ConcurrentCallsWithReusableToken_DoNotLogInAgain()
    {
        var handler = new SequentialLoginHandler(leaseDurations: [3600]);
        using var provider = CreateProvider(handler);
        await provider.GetTokenAsync();

        var tokens = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => provider.GetTokenAsync()));

        Assert.All(tokens, token => Assert.Equal("token-1", token));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTokenIsInsideRefreshWindow_RefreshesToken()
    {
        var handler = new SequentialLoginHandler(leaseDurations: [300, 3600]);
        using var provider = CreateProvider(handler);

        await provider.GetTokenAsync();
        var refreshedToken = await provider.GetTokenAsync();
        var cachedToken = await provider.GetTokenAsync();

        Assert.Equal("token-2", refreshedToken);
        Assert.Equal("token-2", cachedToken);
        Assert.Equal(2, handler.RequestCount);
    }

    private static AppRoleAuthProvider CreateProvider(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultAuthClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("http://localhost:8200")
            });

        return new AppRoleAuthProvider(
            Options.Create(new AppRoleAuthenticationOptions
            {
                RoleId = "role-id",
                SecretId = "secret-id",
                AppRolePath = "approle"
            }),
            factory.Object,
            Mock.Of<ILogger<AppRoleAuthProvider>>());
    }

    private sealed class BlockingLoginHandler(int leaseDuration) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _loginStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseLogin = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public Task LoginStarted => _loginStarted.Task;

        public void ReleaseLogin() => _releaseLogin.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            _loginStarted.TrySetResult();
            await _releaseLogin.Task.WaitAsync(cancellationToken);

            return CreateLoginResponse("cached-token", leaseDuration);
        }
    }

    private sealed class SequentialLoginHandler(params int[] leaseDurations) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestNumber = Interlocked.Increment(ref _requestCount);
            var leaseDuration = requestNumber <= leaseDurations.Length
                ? leaseDurations[requestNumber - 1]
                : leaseDurations[^1];

            return Task.FromResult(CreateLoginResponse($"token-{requestNumber}", leaseDuration));
        }
    }

    private static HttpResponseMessage CreateLoginResponse(string token, int leaseDuration)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    auth = new
                    {
                        client_token = token,
                        lease_duration = leaseDuration
                    }
                }))
        };
    }
}
