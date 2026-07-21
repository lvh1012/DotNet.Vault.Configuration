using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Options;

namespace DotNet.Vault.Configuration.Authentication;

/// <summary>
/// Stub authentication provider for the Vault <c>aws</c> auth method.
/// </summary>
/// <remarks>
/// AWS IAM authentication requires the AWS SDK to sign the <c>iam</c> login
/// request and is not implemented in the initial version of this library.
/// Selecting this provider at runtime will cause
/// <see cref="GetTokenAsync"/> to throw <see cref="NotImplementedException"/>.
/// </remarks>
public class AwsIamAuthProvider : IVaultAuthenticationProvider
{
    private readonly AwsIamAuthenticationOptions _options;

    /// <summary>
    /// Creates a new <see cref="AwsIamAuthProvider"/> bound to the supplied
    /// options.
    /// </summary>
    /// <param name="options">The AWS IAM authentication options.</param>
    public AwsIamAuthProvider(IOptions<AwsIamAuthenticationOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public string AuthenticationMethod => "aws-iam";

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">
    /// Always thrown. AWS IAM authentication is not yet implemented.
    /// </exception>
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // AWS IAM authentication requires AWS SDK - not implemented in this initial version
        throw new NotImplementedException("AWS IAM authentication is not yet implemented");
    }

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
}
