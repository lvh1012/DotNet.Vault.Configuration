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
/// <see cref="VaultClient"/>, <see cref="SecretRefresher"/>, and
/// <see cref="ILogger{T}"/> needed by the provider. The
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
        services.AddSingleton(sp => new Lazy<VaultClient>(() => sp.GetRequiredService<VaultClient>()));
        services.AddSingleton<SecretRefresher>();

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

        var serviceProvider = services.BuildServiceProvider();

        var client = serviceProvider.GetRequiredService<VaultClient>();
        var refresher = serviceProvider.GetRequiredService<SecretRefresher>();
        var logger = serviceProvider.GetRequiredService<ILogger<VaultConfigurationProvider>>();

        return new VaultConfigurationProvider(client, Options, refresher, logger);
    }
}
