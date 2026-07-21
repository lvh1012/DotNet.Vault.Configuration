namespace DotNet.Vault.Configuration.Core.Exceptions;

/// <summary>
/// Thrown when an operation is attempted against a sealed Vault server.
/// </summary>
/// <remarks>
/// A sealed Vault cannot serve secrets or perform most operations until an operator
/// has supplied enough unseal shares. Callers should surface this to operators rather
/// than retrying blindly.
/// </remarks>
public class VaultSealedException : VaultException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VaultSealedException"/> class
    /// with a default message instructing the operator to unseal Vault.
    /// </summary>
    public VaultSealedException()
        : base("Vault is sealed. Unseal Vault before accessing secrets.") { }
}
