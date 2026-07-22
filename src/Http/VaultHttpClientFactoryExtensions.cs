using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

namespace DotNet.Vault.Configuration.Http;

/// <summary>
/// Extension methods for registering a named HttpClient configured for Vault.
/// </summary>
public static class VaultHttpClientFactoryExtensions
{
    /// <summary>
    /// The named HttpClient identifier for authenticated Vault clients.
    /// </summary>
    public const string VaultClientName = "vault-client";

    /// <summary>
    /// The named HttpClient identifier for unauthenticated Vault login requests.
    /// </summary>
    public const string VaultAuthClientName = "vault-auth-client";

    /// <summary>
    /// Register named HttpClients configured for Vault.
    /// <c>vault-client</c> includes the auth delegating handler and Polly retry.
    /// <c>vault-auth-client</c> is unauthenticated and used by auth providers
    /// to call login endpoints without recursively invoking the auth handler.
    /// </summary>
    public static IHttpClientBuilder AddVaultHttpClient(
        this IServiceCollection services,
        VaultOptions options)
    {
        services.AddTransient<VaultAuthDelegatingHandler>();
        services.TryAddSingleton<IVaultAuthenticationProvider>(sp =>
            new TokenAuthProvider(Options.Create(options.Authentication.Token ?? new TokenAuthenticationOptions())));

        services.AddHttpClient(VaultAuthClientName, client =>
        {
            client.BaseAddress = options.Uri;
            client.Timeout = options.Timeout;
        })
        .ConfigurePrimaryHttpMessageHandler(() => CreatePrimaryHandler(options));

        return services.AddHttpClient(VaultClientName, client =>
        {
            client.BaseAddress = options.Uri;
            client.Timeout = options.Timeout;
        })
        .ConfigurePrimaryHttpMessageHandler(() => CreatePrimaryHandler(options))
        .AddHttpMessageHandler<VaultAuthDelegatingHandler>()
        .AddPolicyHandler((sp, request) =>
        {
            var logger = sp.GetRequiredService<ILogger<VaultClient>>();
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => (int)msg.StatusCode == 412)
                .WaitAndRetryAsync(
                    retryCount: options.Refresh.Retry.MaxRetries,
                    sleepDurationProvider: attempt =>
                    {
                        var delay = TimeSpan.FromTicks(
                            (long)(options.Refresh.Retry.InitialInterval.Ticks *
                            Math.Pow(options.Refresh.Retry.Multiplier, attempt - 1)));
                        return delay > options.Refresh.Retry.MaxInterval
                            ? options.Refresh.Retry.MaxInterval
                            : delay;
                    },
                    onRetry: (outcome, delay, attempt, _) =>
                    {
                        logger.LogWarning(
                            "Vault request failed (attempt {Attempt}/{Max}). Retrying in {Delay}",
                            attempt, options.Refresh.Retry.MaxRetries, delay);
                    });
        });
    }

    private static HttpMessageHandler CreatePrimaryHandler(VaultOptions options)
    {
        var ssl = options.Ssl;
        var handler = new SocketsHttpHandler();
        List<X509Certificate2>? ownedCertificates = null;

        try
        {
            var sslOptions = handler.SslOptions;
            sslOptions.EnabledSslProtocols = ssl.Protocol;
            sslOptions.CertificateRevocationCheckMode = ssl.CheckCertificateRevocation
                ? X509RevocationMode.Online
                : X509RevocationMode.NoCheck;
            sslOptions.TargetHost = ssl.ServerName ?? options.Uri.Host;

            var clientCertificate = ssl.ClientCertificate;
            if (clientCertificate is null && ssl.ClientCertificatePath is not null)
            {
                clientCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                    ssl.ClientCertificatePath,
                    ssl.ClientCertificatePassword);
                (ownedCertificates ??= []).Add(clientCertificate);
            }

            if (clientCertificate is not null)
            {
                sslOptions.ClientCertificates = new X509CertificateCollection { clientCertificate };
            }

            var caCertificate = ssl.CaCertificate;
            if (caCertificate is null && ssl.CaCertificatePath is not null)
            {
                caCertificate = X509CertificateLoader.LoadCertificateFromFile(ssl.CaCertificatePath);
                (ownedCertificates ??= []).Add(caCertificate);
            }

            if (caCertificate is not null)
            {
                var chainPolicy = new X509ChainPolicy
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust
                };
                chainPolicy.CustomTrustStore.Add(caCertificate);
                sslOptions.CertificateChainPolicy = chainPolicy;
            }

            if (ssl.SkipVerify)
            {
                sslOptions.RemoteCertificateValidationCallback =
                    static (_, _, _, _) => true;
            }

            return ownedCertificates is null
                ? handler
                : new CertificateDisposingHandler(handler, ownedCertificates);
        }
        catch
        {
            handler.Dispose();

            if (ownedCertificates is not null)
            {
                foreach (var certificate in ownedCertificates)
                {
                    certificate.Dispose();
                }
            }

            throw;
        }
    }
    private sealed class CertificateDisposingHandler : DelegatingHandler
    {
        private readonly List<X509Certificate2> _ownedCertificates;

        public CertificateDisposingHandler(
            HttpMessageHandler innerHandler,
            List<X509Certificate2> ownedCertificates)
        {
            InnerHandler = innerHandler;
            _ownedCertificates = ownedCertificates;
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                base.Dispose(disposing);
                return;
            }

            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                foreach (var certificate in _ownedCertificates)
                {
                    certificate.Dispose();
                }
            }
        }
    }

}
