using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Http;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;

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
/// <see cref="VaultClient"/>, <see cref="VaultLeaseRenewer"/>, <see cref="SecretRefresher"/>,
/// and <see cref="ILogger{T}"/> needed by the provider. The provider owns this
/// service provider and disposes it when its configuration lifetime ends.
/// <c>Microsoft.Extensions.Options.Options.Create{T}(T)</c> wrapper is
/// used to bind each authentication options block to its corresponding
/// <see cref="IVaultAuthenticationProvider"/> implementation without
/// depending on the caller's options pipeline.
/// </remarks>
public class VaultConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// Gets or sets the <see cref="VaultOptions"/> used to configure the
    /// resulting <see cref="VaultConfigurationProvider"/>.
    /// </summary>
    public VaultOptions Options { get; set; } = new();

    /// <summary>
    /// Gets or sets an optional factory for the service provider owned by the
    /// configuration provider.
    /// </summary>
    /// <remarks>
    /// When omitted, <see cref="Build"/> creates the standard Vault service
    /// composition from <see cref="Options"/>.
    /// </remarks>
    public Func<IServiceProvider>? ServiceProviderFactory { get; set; }

    /// <summary>
    /// Builds a <see cref="VaultConfigurationProvider"/> pre-populated with the
    /// authentication providers, secret backends, and dependencies declared in
    /// <see cref="Options"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> that is requesting the source.</param>
    /// <returns>The configured <see cref="VaultConfigurationProvider"/>.</returns>
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        if (ServiceProviderFactory is not null)
            return CreateProvider(ServiceProviderFactory());

        var services = new ServiceCollection();

        services.AddSingleton(Options);
        services.AddSingleton(sp => new VaultClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            Options,
            sp.GetServices<IVaultAuthenticationProvider>(),
            sp.GetServices<IVaultSecretBackend>(),
            sp.GetRequiredService<ILogger<VaultClient>>()));
        services.AddSingleton<ISecretRefreshScheduler, TimerSecretRefreshScheduler>();
        services.AddSingleton<VaultLeaseRenewer>();
        services.AddSingleton(sp => new SecretRefresher(
            Options,
            sp.GetRequiredService<ILogger<SecretRefresher>>(),
            sp.GetRequiredService<ISecretRefreshScheduler>(),
            sp.GetRequiredService<VaultLeaseRenewer>()));

        // Register the named Vault HttpClient, auth delegating handler, and fallback token provider.
        services.AddVaultHttpClient(Options);

        if (Options.Authentication.AppRole != null)
            services.AddSingleton<IVaultAuthenticationProvider>(sp =>
                new AppRoleAuthProvider(
                    Microsoft.Extensions.Options.Options.Create(Options.Authentication.AppRole),
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<ILogger<AppRoleAuthProvider>>()));

        if (Options.Authentication.Kubernetes != null)
            services.AddSingleton<IVaultAuthenticationProvider>(sp =>
                new KubernetesAuthProvider(
                    Microsoft.Extensions.Options.Options.Create(Options.Authentication.Kubernetes),
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<ILogger<KubernetesAuthProvider>>()));

        if (Options.Authentication.Ldap != null)
            services.AddSingleton<IVaultAuthenticationProvider>(sp =>
                new LdapAuthProvider(
                    Microsoft.Extensions.Options.Options.Create(Options.Authentication.Ldap),
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<ILogger<LdapAuthProvider>>()));

        if (Options.Authentication.AwsIam != null)
            services.AddSingleton<IVaultAuthenticationProvider>(sp =>
                new AwsIamAuthProvider(Microsoft.Extensions.Options.Options.Create(Options.Authentication.AwsIam)));

        if (Options.Authentication.TlsCertificate != null)
            services.AddSingleton<IVaultAuthenticationProvider>(sp =>
                new TlsCertificateAuthProvider(Microsoft.Extensions.Options.Options.Create(Options.Authentication.TlsCertificate)));

        if (Options.Kv.Enabled)
            services.AddSingleton<IVaultSecretBackend>(sp =>
                new KvSecretBackend(
                    Options.Kv,
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<SecretRefresher>()));

        if (Options.Database.Enabled)
            services.AddSingleton<IVaultSecretBackend>(sp =>
                new DatabaseSecretBackend(
                    Options.Database,
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<SecretRefresher>()));

        if (Options.Pki.Enabled)
            services.AddSingleton<IVaultSecretBackend>(sp =>
                new PkiSecretBackend(
                    Options.Pki,
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<SecretRefresher>()));

        services.AddLogging();

        return CreateProvider(services.BuildServiceProvider());
    }

    private VaultConfigurationProvider CreateProvider(IServiceProvider serviceProvider)
    {
        if (serviceProvider is not IDisposable disposableServiceProvider)
        {
            throw new InvalidOperationException(
                "ServiceProviderFactory must return an IDisposable service provider.");
        }

        var client = serviceProvider.GetRequiredService<VaultClient>();
        var refresher = serviceProvider.GetRequiredService<SecretRefresher>();
        var logger = serviceProvider.GetRequiredService<ILogger<VaultConfigurationProvider>>();

        return new VaultConfigurationProvider(client, Options, refresher, logger, disposableServiceProvider);
    }
}
