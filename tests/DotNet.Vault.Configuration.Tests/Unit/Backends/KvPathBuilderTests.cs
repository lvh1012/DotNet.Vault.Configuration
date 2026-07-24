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

    [Fact]
    public void BuildPaths_WithDefaultOptions_ReturnsV2DefaultContextPath()
    {
        // Act
        var paths = KvPathBuilder.BuildPaths(new KvSecretBackendOptions());

        // Assert
        Assert.Equal(new[] { "secret/data/application" }, paths);
    }

    [Fact]
    public void BuildPaths_WithV1AndDefaultContext_ReturnsUnprefixedDefaultContextPath()
    {
        // Arrange
        var options = new KvSecretBackendOptions
        {
            Version = 1
        };

        // Act
        var paths = KvPathBuilder.BuildPaths(options);

        // Assert
        Assert.Equal(new[] { "secret/application" }, paths);
    }

    [Fact]
    public void BuildPaths_WithTrailingSlashesInV2BackendPath_NormalizesBeforeDataSegment()
    {
        // Arrange
        var options = new KvSecretBackendOptions
        {
            BackendPath = "secret///"
        };

        // Act
        var paths = KvPathBuilder.BuildPaths(options);

        // Assert
        Assert.Equal(new[] { "secret/data/application" }, paths);
    }

    [Fact]
    public void BuildPaths_WithEmptyDefaultContext_OnlyGeneratesApplicationPaths()
    {
        // Arrange
        var options = new KvSecretBackendOptions
        {
            DefaultContext = string.Empty,
            ApplicationName = "catalog",
            Profiles = new List<string> { "development" }
        };

        // Act
        var paths = KvPathBuilder.BuildPaths(options);

        // Assert
        Assert.Equal(
            new[] { "secret/data/catalog", "secret/data/catalog/development" },
            paths);
    }

    [Fact]
    public void BuildPaths_WithEmptyApplicationName_OnlyGeneratesContextPaths()
    {
        // Arrange
        var options = new KvSecretBackendOptions
        {
            DefaultContext = "shared",
            ApplicationName = string.Empty,
            Profiles = new List<string> { "development" }
        };

        // Act
        var paths = KvPathBuilder.BuildPaths(options);

        // Assert
        Assert.Equal(
            new[] { "secret/data/shared", "secret/data/shared/development" },
            paths);
    }

    [Fact]
    public void BuildPaths_WithBackendName_ComposesApplicationAndBackendScope()
    {
        // Arrange
        var options = new KvSecretBackendOptions
        {
            DefaultContext = string.Empty,
            ApplicationName = "orders",
            BackendName = "payments"
        };

        // Act
        var paths = KvPathBuilder.BuildPaths(options);

        // Assert
        Assert.Equal(
            new[] { "secret/data/orders", "secret/data/orders-payments" },
            paths);
    }

    [Fact]
    public void BuildPaths_WithCustomProfileSeparator_PreservesDeclaredProfileOrder()
    {
        // Arrange
        var options = new KvSecretBackendOptions
        {
            DefaultContext = "shared",
            Profiles = new List<string> { "blue", "green" },
            ProfileSeparator = "-"
        };

        // Act
        var paths = KvPathBuilder.BuildPaths(options);

        // Assert
        Assert.Equal(
            new[]
            {
                "secret/data/shared",
                "secret/data/shared-blue",
                "secret/data/shared-green"
            },
            paths);
    }

    [Fact]
    public void BuildPaths_WithNoConfiguredScopes_ReturnsNoPaths()
    {
        // Arrange
        var options = new KvSecretBackendOptions
        {
            DefaultContext = string.Empty,
            ApplicationName = string.Empty,
            BackendName = string.Empty,
            Profiles = new List<string> { "development" }
        };

        // Act
        var paths = KvPathBuilder.BuildPaths(options);

        // Assert
        Assert.Empty(paths);
    }
}
