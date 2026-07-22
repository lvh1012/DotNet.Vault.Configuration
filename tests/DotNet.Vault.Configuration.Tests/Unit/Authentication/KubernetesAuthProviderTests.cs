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

public sealed class KubernetesAuthProviderTests
{
    [Fact]
    public async Task GetTokenAsync_ConcurrentCacheMisses_OnlyOneLoginHappens()
    {
        var handler = new BlockingLoginHandler(leaseDuration: 3600);
        await using var fixture = await KubernetesProviderFixture.CreateAsync(handler);
        using var provider = fixture.CreateProvider();

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
    public async Task GetTokenAsync_WithReusableCachedToken_DoesNotLogInAgain()
    {
        var handler = new SequentialLoginHandler(leaseDurations: [3600]);
        await using var fixture = await KubernetesProviderFixture.CreateAsync(handler);
        using var provider = fixture.CreateProvider();

        var firstToken = await provider.GetTokenAsync();
        var cachedToken = await provider.GetTokenAsync();

        Assert.Equal("token-1", firstToken);
        Assert.Equal("token-1", cachedToken);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetTokenAsync_WhenCachedTokenExpires_RenewsToken()
    {
        var handler = new SequentialLoginHandler(leaseDurations: [0, 3600]);
        await using var fixture = await KubernetesProviderFixture.CreateAsync(handler);
        using var provider = fixture.CreateProvider();

        var expiredToken = await provider.GetTokenAsync();
        var renewedToken = await provider.GetTokenAsync();
        var cachedRenewedToken = await provider.GetTokenAsync();

        Assert.Equal("token-1", expiredToken);
        Assert.Equal("token-2", renewedToken);
        Assert.Equal("token-2", cachedRenewedToken);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task IsTokenValidAsync_ReturnsFalseWhenCachedTokenHasExpired()
    {
        var handler = new SequentialLoginHandler(leaseDurations: [0]);
        await using var fixture = await KubernetesProviderFixture.CreateAsync(handler);
        using var provider = fixture.CreateProvider();

        await provider.GetTokenAsync();

        Assert.False(await provider.IsTokenValidAsync());
    }

    private sealed class KubernetesProviderFixture : IAsyncDisposable
    {
        private readonly Mock<IHttpClientFactory> _httpClientFactory;
        private readonly string _serviceAccountTokenPath;

        private KubernetesProviderFixture(Mock<IHttpClientFactory> httpClientFactory, string serviceAccountTokenPath)
        {
            _httpClientFactory = httpClientFactory;
            _serviceAccountTokenPath = serviceAccountTokenPath;
        }

        public static async Task<KubernetesProviderFixture> CreateAsync(HttpMessageHandler handler)
        {
            var serviceAccountTokenPath = Path.GetTempFileName();
            await File.WriteAllTextAsync(serviceAccountTokenPath, "service-account-jwt");

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory
                .Setup(x => x.CreateClient(VaultHttpClientFactoryExtensions.VaultAuthClientName))
                .Returns(() => new HttpClient(handler, disposeHandler: false)
                {
                    BaseAddress = new Uri("http://localhost:8200")
                });

            return new KubernetesProviderFixture(httpClientFactory, serviceAccountTokenPath);
        }

        public KubernetesAuthProvider CreateProvider()
        {
            return new KubernetesAuthProvider(
                Options.Create(new KubernetesAuthenticationOptions
                {
                    Role = "role",
                    KubernetesRolePath = "kubernetes",
                    ServiceAccountTokenPath = _serviceAccountTokenPath
                }),
                _httpClientFactory.Object,
                Mock.Of<ILogger<KubernetesAuthProvider>>());
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(_serviceAccountTokenPath);
            return ValueTask.CompletedTask;
        }
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
