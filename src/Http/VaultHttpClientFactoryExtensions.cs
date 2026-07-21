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
        });

        return services.AddHttpClient(VaultClientName, client =>
        {
            client.BaseAddress = options.Uri;
            client.Timeout = options.Timeout;
        })
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
}
