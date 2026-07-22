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
/// Authentication provider for the Vault <c>ldap</c> auth method.
/// </summary>
/// <remarks>
/// Performs the <c>POST /v1/auth/{mount}/login/{username}</c> exchange with the
/// configured <see cref="LdapAuthenticationOptions.Username"/> and
/// <see cref="LdapAuthenticationOptions.Password"/>, caches the returned client
/// token, and refreshes it before its lease expires.
/// </remarks>
public class LdapAuthProvider : IVaultAuthenticationProvider, IDisposable
{
    private readonly LdapAuthenticationOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LdapAuthProvider> _logger;
    private readonly SemaphoreSlim _tokenRefreshLock = new(1, 1);
    private TokenCacheEntry? _tokenCache;
    /// <summary>
    /// Creates a new <see cref="LdapAuthProvider"/> bound to the supplied
    /// options, <see cref="IHttpClientFactory"/>, and logger.
    /// </summary>
    /// <param name="options">The LDAP authentication options.</param>
    /// <param name="httpClientFactory">The HTTP client factory used to create Vault API clients.</param>
    /// <param name="logger">The logger used for diagnostic output.</param>
    public LdapAuthProvider(
        IOptions<LdapAuthenticationOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<LdapAuthProvider> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string AuthenticationMethod => "ldap";

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
                ?? throw new VaultAuthenticationException("ldap", "Failed to obtain token");
        }
        finally
        {
            _tokenRefreshLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var payload = new { password = _options.Password };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");

        using var client = _httpClientFactory.CreateClient(VaultHttpClientFactoryExtensions.VaultAuthClientName);
        var response = await client.PostAsync($"/v1/auth/{_options.LdapPath}/login/{_options.Username}", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

        var token = result.GetProperty("auth").GetProperty("client_token").GetString();
        var leaseDuration = result.GetProperty("auth").GetProperty("lease_duration").GetInt32();
        var expiry = DateTimeOffset.UtcNow.AddSeconds(leaseDuration);

        Volatile.Write(ref _tokenCache, new TokenCacheEntry(token, expiry));

        _logger.LogInformation(
            "Refreshed LDAP Vault token; lease duration {LeaseDuration}s",
            leaseDuration);
    }

    /// <inheritdoc />
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default)
    {
        var cachedToken = Volatile.Read(ref _tokenCache);
        return Task.FromResult(cachedToken?.Token != null && cachedToken.Expiry > DateTimeOffset.UtcNow);
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
