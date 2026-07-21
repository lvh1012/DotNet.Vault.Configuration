using Microsoft.Extensions.Configuration;

namespace DotNet.Vault.Configuration.Core.Extensions;

/// <summary>
/// <see cref="IConfigurationBuilder"/> extension methods that register a
/// <see cref="VaultConfigurationSource"/>.
/// </summary>
/// <remarks>
/// The two overloads accept either a strongly-typed
/// <see cref="Action{VaultOptions}"/> callback or an existing
/// <see cref="IConfiguration"/> instance whose <c>Vault</c> section will be
/// bound to <see cref="VaultOptions"/>.
/// </remarks>
public static class VaultConfigurationExtensions
{
    /// <summary>
    /// Registers a <see cref="VaultConfigurationSource"/> on the supplied
    /// <see cref="IConfigurationBuilder"/>, configuring
    /// <see cref="VaultOptions"/> through the supplied delegate.
    /// </summary>
    /// <param name="builder">The configuration builder to extend.</param>
    /// <param name="configure">A callback that configures the <see cref="VaultOptions"/> instance.</param>
    /// <returns>The same <paramref name="builder"/> for fluent chaining.</returns>
    public static IConfigurationBuilder AddVault(
        this IConfigurationBuilder builder,
        Action<VaultOptions> configure)
    {
        var source = new VaultConfigurationSource();
        configure(source.Options);
        builder.Add(source);
        return builder;
    }

    /// <summary>
    /// Registers a <see cref="VaultConfigurationSource"/> on the supplied
    /// <see cref="IConfigurationBuilder"/>, binding the
    /// <see cref="VaultOptions"/> from the supplied
    /// <see cref="IConfiguration"/>.
    /// </summary>
    /// <param name="builder">The configuration builder to extend.</param>
    /// <param name="configuration">The configuration instance to bind against.</param>
    /// <returns>The same <paramref name="builder"/> for fluent chaining.</returns>
    public static IConfigurationBuilder AddVault(
        this IConfigurationBuilder builder,
        IConfiguration configuration)
    {
        var source = new VaultConfigurationSource();
        configuration.Bind(source.Options);
        builder.Add(source);
        return builder;
    }
}
