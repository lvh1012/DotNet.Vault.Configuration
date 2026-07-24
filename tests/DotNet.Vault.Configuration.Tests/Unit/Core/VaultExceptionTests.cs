using DotNet.Vault.Configuration.Core.Exceptions;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Core;

public class VaultExceptionTests
{
    [Fact]
    public void VaultException_ConstructorPreservesMessageAndInnerException()
    {
        var innerException = new InvalidOperationException("transport failed");

        var exception = new VaultException("Vault operation failed", innerException);

        Assert.Equal("Vault operation failed", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void VaultApiException_ConstructorExposesResponseDiagnostics()
    {
        var exception = new VaultApiException(403, "permission denied", "permission-denied", "request-123");

        Assert.Equal("Vault API error (HTTP 403): permission denied", exception.Message);
        Assert.Equal(403, exception.StatusCode);
        Assert.Equal("permission-denied", exception.ErrorCode);
        Assert.Equal("request-123", exception.RequestId);
    }

    [Fact]
    public void VaultAuthenticationException_ConstructorPreservesMethodAndInnerException()
    {
        var innerException = new UnauthorizedAccessException("token rejected");

        var exception = new VaultAuthenticationException("kubernetes", "login failed", innerException);

        Assert.Equal("Authentication failed for method 'kubernetes': login failed", exception.Message);
        Assert.Equal("kubernetes", exception.AuthenticationMethod);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void VaultBackendNotSupportedException_ConstructorExposesBackendContext()
    {
        var exception = new VaultBackendNotSupportedException("database");

        Assert.Equal("Secret backend 'database' is not supported or not enabled", exception.Message);
        Assert.Equal("database", exception.BackendType);
    }

    [Fact]
    public void VaultConnectionException_ConstructorPreservesTargetUriAndInnerException()
    {
        var vaultUri = new Uri("https://vault.example.test:8200");
        var innerException = new HttpRequestException("connection refused");

        var exception = new VaultConnectionException(vaultUri, innerException);

        Assert.Equal($"Failed to connect to Vault at {vaultUri}", exception.Message);
        Assert.Same(vaultUri, exception.VaultUri);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void VaultLeaseRenewalException_ConstructorPreservesLeaseContextAndInnerException()
    {
        var innerException = new InvalidOperationException("lease endpoint rejected renewal");

        var exception = new VaultLeaseRenewalException("lease-abc", "renewal failed", innerException);

        Assert.Equal("Failed to renew lease 'lease-abc': renewal failed", exception.Message);
        Assert.Equal("lease-abc", exception.LeaseId);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void VaultSealedException_ConstructorInstructsOperatorToUnseal()
    {
        var exception = new VaultSealedException();

        Assert.Equal("Vault is sealed. Unseal Vault before accessing secrets.", exception.Message);
    }

    [Fact]
    public void VaultSecretNotFoundException_ConstructorExposesPathAndDiagnosticMessage()
    {
        var exception = new VaultSecretNotFoundException("kv/application/api", "secret was deleted");

        Assert.Equal("Secret not found at path 'kv/application/api': secret was deleted", exception.Message);
        Assert.Equal("kv/application/api", exception.Path);
    }
}
