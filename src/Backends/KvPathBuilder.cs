using DotNet.Vault.Configuration.Core;

namespace DotNet.Vault.Configuration.Backends;

/// <summary>
/// Builds the ordered set of logical Vault paths that the Key/Value (KV) backend
/// should read for a given <see cref="KvSecretBackendOptions"/> configuration.
/// </summary>
/// <remarks>
/// The strategy mirrors the Spring Cloud Vault convention: for each scope
/// (default context, application name, named backend) the builder emits a
/// base path plus one path per active profile. KV v2 paths are prefixed with
/// <c>data</c>; KV v1 paths are not.
/// </remarks>
public static class KvPathBuilder
{
    /// <summary>
    /// Computes the logical Vault paths to read for the supplied KV options.
    /// </summary>
    /// <param name="options">The KV backend options describing the mount path, version, and scope.</param>
    /// <returns>The list of logical paths, in lookup order.</returns>
    public static List<string> BuildPaths(KvSecretBackendOptions options)
    {
        var paths = new List<string>();
        var backendPath = options.BackendPath.TrimEnd('/');
        var pathPrefix = options.Version == 2 ? $"{backendPath}/data" : backendPath;

        if (!string.IsNullOrEmpty(options.DefaultContext))
        {
            paths.Add($"{pathPrefix}/{options.DefaultContext}".TrimEnd('/'));

            foreach (var profile in options.Profiles)
            {
                paths.Add($"{pathPrefix}/{options.DefaultContext}{options.ProfileSeparator}{profile}".TrimEnd('/'));
            }
        }

        if (!string.IsNullOrEmpty(options.ApplicationName))
        {
            paths.Add($"{pathPrefix}/{options.ApplicationName}".TrimEnd('/'));

            foreach (var profile in options.Profiles)
            {
                paths.Add($"{pathPrefix}/{options.ApplicationName}{options.ProfileSeparator}{profile}".TrimEnd('/'));
            }
        }

        if (!string.IsNullOrEmpty(options.BackendName))
        {
            var namedScope = $"{options.ApplicationName}-{options.BackendName}";
            paths.Add($"{pathPrefix}/{namedScope}".TrimEnd('/'));

            foreach (var profile in options.Profiles)
            {
                paths.Add($"{pathPrefix}/{namedScope}{options.ProfileSeparator}{profile}".TrimEnd('/'));
            }
        }

        return paths;
    }
}
