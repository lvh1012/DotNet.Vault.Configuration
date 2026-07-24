using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Exceptions;
using DotNet.Vault.Configuration.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace DotNet.Vault.Configuration.Authentication;

/// <summary>
/// Authentication provider for the Vault <c>approle</c> auth method.
/// </summary>
/// <remarks>
/// Performs the <c>POST /v1/auth/{mount}/login</c> exchange with the
/// configured <see cref="AppRoleAuthenticationOptions.RoleId"/> and
/// <see cref="AppRoleAuthenticationOptions.SecretId"/>, caches the returned
/// client token, and refreshes it before its lease expires.
/// </remarks>
public class AppRoleAuthProvider : IVaultAuthenticationProvider, IDisposable
{
    private readonly AppRoleAuthenticationOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AppRoleAuthProvider> _logger;
    private readonly SemaphoreSlim _tokenRefreshLock = new(1, 1);
    private TokenCacheEntry? _tokenCache;

    /// <summary>
    /// Creates a new <see cref="AppRoleAuthProvider"/> bound to the supplied
    /// options, <see cref="IHttpClientFactory"/>, and logger.
    /// </summary>
    /// <param name="options">The AppRole authentication options.</param>
    /// <param name="httpClientFactory">The HTTP client factory used to create Vault API clients.</param>
    /// <param name="logger">The logger used for diagnostic output.</param>
    public AppRoleAuthProvider(
        IOptions<AppRoleAuthenticationOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AppRoleAuthProvider> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }
    /// <summary>
    /// Creates a new <see cref="AppRoleAuthProvider"/> bound to the supplied options and HTTP client.
    /// </summary>
    /// <param name="options">The AppRole authentication options.</param>
    /// <param name="httpClient">The HTTP client used to call the Vault login endpoint.</param>
    public AppRoleAuthProvider(
        IOptions<AppRoleAuthenticationOptions> options,
        HttpClient httpClient)
        : this(options, new SingleHttpClientFactory(httpClient), Microsoft.Extensions.Logging.Abstractions.NullLogger<AppRoleAuthProvider>.Instance)
    {
    }


    /// <inheritdoc />
    public string AuthenticationMethod => "approle";

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var cachedToken = Volatile.Read(ref _tokenCache);
        if (cachedToken is { Token: { } cachedTokenValue } && CanReuse(cachedToken))
        {
            return cachedTokenValue;
        }

        await _tokenRefreshLock.WaitAsync(cancellationToken);
        try
        {
            cachedToken = Volatile.Read(ref _tokenCache);
            if (cachedToken is { Token: { } refreshedCachedTokenValue } && CanReuse(cachedToken))
            {
                return refreshedCachedTokenValue;
            }

            await RefreshAsync(cancellationToken);
            cachedToken = Volatile.Read(ref _tokenCache);
            return cachedToken?.Token
                ?? throw new VaultAuthenticationException("approle", "Failed to obtain token");
        }
        finally
        {
            _tokenRefreshLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            role_id = _options.RoleId,
            secret_id = _options.SecretId
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");

        var client = _httpClientFactory.CreateClient(VaultHttpClientFactoryExtensions.VaultAuthClientName);
        var response = await client.PostAsync($"/v1/auth/{_options.AppRolePath}/login", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

        var token = result.GetProperty("auth").GetProperty("client_token").GetString();
        var leaseDuration = result.GetProperty("auth").GetProperty("lease_duration").GetInt32();
        var expiry = DateTimeOffset.UtcNow.AddSeconds(leaseDuration);

        Volatile.Write(ref _tokenCache, new TokenCacheEntry(token, expiry));

        _logger.LogInformation(
            "Refreshed AppRole Vault token; lease duration {LeaseDuration}s",
            leaseDuration);
    }

    /// <inheritdoc />
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default)
    {
        var cachedToken = Volatile.Read(ref _tokenCache);
        return Task.FromResult(
            cachedToken?.Token != null && cachedToken.Expiry > DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Releases the synchronization primitive used to serialize token refreshes.
    /// </summary>
    public void Dispose()
    {
        _tokenRefreshLock.Dispose();
    }

    private static bool CanReuse(TokenCacheEntry? cachedToken)
    {
        return cachedToken?.Token != null
            && cachedToken.Expiry > DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private sealed record TokenCacheEntry(string? Token, DateTimeOffset Expiry);
}
