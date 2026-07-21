using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNet.Vault.Configuration.Core;

/// <summary>
/// <see cref="IConfigurationSource"/> that builds a
/// <see cref="VaultConfigurationProvider"/> from a populated
/// <see cref="VaultOptions"/> instance.
/// </summary>
/// <remarks>
/// <see cref="Build"/> stands up a self-contained
/// <see cref="ServiceProvider"/> containing the authentication providers and
/// secret backends required by the requested backends, then resolves the
/// <see cref="VaultClient"/>, <see cref="SecretRefresher"/>, and
/// <see cref="ILogger{T}"/> needed by the provider. The
/// <see cref="Microsoft.Extensions.Options.Options.Create{T}(T)"/> wrapper is
/// used to bind <see cref="TokenAuthenticationOptions"/> for
/// <see cref="TokenAuthProvider"/> without depending on the caller's options
/// pipeline.
/// </remarks>
public class VaultConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// Gets or sets the <see cref="VaultOptions"/> used to configure the
    /// resulting <see cref="VaultConfigurationProvider"/>.
    /// </summary>
    public VaultOptions Options { get; set; } = new();

    /// <summary>
    /// Builds a <see cref="VaultConfigurationProvider"/> pre-populated with the
    /// authentication providers, secret backends, and dependencies declared in
    /// <see cref="Options"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> that is requesting the source.</param>
    /// <returns>The configured <see cref="VaultConfigurationProvider"/>.</returns>
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        var services = new ServiceCollection();

        services.AddSingleton(Options);
        services.AddSingleton<VaultClient>();
        services.AddSingleton<SecretRefresher>();

        if (Options.Authentication.Token != null)
            services.AddSingleton<IVaultAuthenticationProvider>(sp =>
                new TokenAuthProvider(Microsoft.Extensions.Options.Options.Create(Options.Authentication.Token)));

        if (Options.Kv.Enabled)
            services.AddSingleton<IVaultSecretBackend>(sp =>
                new KvSecretBackend(Options.Kv, new HttpClient { BaseAddress = Options.Uri }));

        if (Options.Database.Enabled)
            services.AddSingleton<IVaultSecretBackend>(sp =>
                new DatabaseSecretBackend(Options.Database, new HttpClient { BaseAddress = Options.Uri }));

        if (Options.Pki.Enabled)
            services.AddSingleton<IVaultSecretBackend>(sp =>
                new PkiSecretBackend(Options.Pki, new HttpClient { BaseAddress = Options.Uri }));

        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();

        var client = serviceProvider.GetRequiredService<VaultClient>();
        var refresher = serviceProvider.GetRequiredService<SecretRefresher>();
        var logger = serviceProvider.GetRequiredService<ILogger<VaultConfigurationProvider>>();

        return new VaultConfigurationProvider(client, Options, refresher, logger);
    }
}
