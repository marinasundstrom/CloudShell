using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDeclarationTests
{
    [Fact]
    public void ConfigureResources_DeclaresTransientResourceByDefault()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShellControlPlane()
            .ConfigureResources(resources =>
            {
                resources
                    .Declare("configuration", "configuration:example")
                    .WithReference("application:api");
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.Equal("configuration", declaration.ProviderId);
        Assert.Equal("configuration:example", declaration.ResourceId);
        Assert.Equal(["application:api"], declaration.DependsOn);
        Assert.Equal(ResourceDeclarationPersistence.Transient, declaration.Persistence);
        Assert.False(declaration.OverwritePersistedState);
    }

    [Fact]
    public void Persist_CanRequestOverwrite()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShellControlPlane()
            .ConfigureResources(resources =>
            {
                resources
                    .Declare("configuration", "configuration:example")
                    .Persist(overwrite: true);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.Equal(ResourceDeclarationPersistence.Persisted, declaration.Persistence);
        Assert.True(declaration.OverwritePersistedState);
    }
}
