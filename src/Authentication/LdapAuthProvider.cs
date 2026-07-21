using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Exceptions;
using DotNet.Vault.Configuration.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text.Json;

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
public class LdapAuthProvider : IVaultAuthenticationProvider
{
    private readonly LdapAuthenticationOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LdapAuthProvider> _logger;
    private string? _cachedToken;
    private DateTimeOffset? _tokenExpiry;

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
        if (_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _cachedToken;
        }

        await RefreshAsync(cancellationToken);
        return _cachedToken ?? throw new VaultAuthenticationException("ldap", "Failed to obtain token");
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var payload = new { password = _options.Password };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");

        var client = _httpClientFactory.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName);
        var response = await client.PostAsync($"/v1/auth/{_options.LdapPath}/login/{_options.Username}", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

        _cachedToken = result.GetProperty("auth").GetProperty("client_token").GetString();

        var leaseDuration = result.GetProperty("auth").GetProperty("lease_duration").GetInt32();
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(leaseDuration);

        _logger.LogInformation(
            "Refreshed LDAP Vault token; lease duration {LeaseDuration}s",
            leaseDuration);
    }

    /// <inheritdoc />
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow);
    }
}
