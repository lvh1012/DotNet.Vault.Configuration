using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Backends;

public class KvPathBuilderTests
{
    [Fact]
    public void BuildPaths_WithApplicationNameAndProfiles_ReturnsCorrectPaths()
    {
        // Arrange
        var options = new KvSecretBackendOptions
        {
            BackendPath = "secret",
            Version = 2,
            ApplicationName = "myapp",
            DefaultContext = "application",
            Profiles = new List<string> { "dev", "prod" }
        };

        // Act
        var paths = KvPathBuilder.BuildPaths(options);

        // Assert
        Assert.Equal(6, paths.Count);
        Assert.Contains("secret/data/application", paths);
        Assert.Contains("secret/data/application/dev", paths);
        Assert.Contains("secret/data/application/prod", paths);
        Assert.Contains("secret/data/myapp", paths);
        Assert.Contains("secret/data/myapp/dev", paths);
        Assert.Contains("secret/data/myapp/prod", paths);
    }

    [Fact]
    public void BuildPaths_WithKvV1_ReturnsCorrectPaths()
    {
        // Arrange
        var options = new KvSecretBackendOptions
        {
            BackendPath = "secret",
            Version = 1,
            ApplicationName = "myapp"
        };

        // Act
        var paths = KvPathBuilder.BuildPaths(options);

        // Assert
        Assert.DoesNotContain(paths, p => p.Contains("/data/"));
        Assert.Contains("secret/application", paths);
        Assert.Contains("secret/myapp", paths);
    }
}
