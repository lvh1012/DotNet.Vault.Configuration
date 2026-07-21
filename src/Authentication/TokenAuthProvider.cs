using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Options;

namespace DotNet.Vault.Configuration.Authentication;

/// <summary>
/// Authentication provider that uses a static Vault token or a runtime
/// delegate to obtain one.
/// </summary>
/// <remarks>
/// When <see cref="TokenAuthenticationOptions.TokenProvider"/> is configured
/// it is invoked on every <see cref="GetTokenAsync"/> call, allowing
/// integration with secret brokers or short-lived token sources. Otherwise the
/// static <see cref="TokenAuthenticationOptions.Token"/> value is returned.
/// </remarks>
public class TokenAuthProvider : IVaultAuthenticationProvider
{
    private readonly TokenAuthenticationOptions _options;

    /// <summary>
    /// Creates a new <see cref="TokenAuthProvider"/> bound to the supplied
    /// options.
    /// </summary>
    /// <param name="options">The token authentication options.</param>
    public TokenAuthProvider(IOptions<TokenAuthenticationOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public string AuthenticationMethod => "token";

    /// <inheritdoc />
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_options.TokenProvider != null)
        {
            return _options.TokenProvider();
        }

        return Task.FromResult(_options.Token);
    }

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(!string.IsNullOrEmpty(_options.Token) || _options.TokenProvider != null);
    }
}
