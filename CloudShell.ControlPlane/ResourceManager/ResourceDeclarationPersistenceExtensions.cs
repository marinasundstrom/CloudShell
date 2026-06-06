using CloudShell.Abstractions.ResourceManager;
using CloudShell.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ControlPlane.ResourceManager;

public static class ResourceDeclarationPersistenceExtensions
{
    public static void PersistProgrammaticResourceDeclarations(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var providers = scope.ServiceProvider
            .GetServices<IResourceProvider>()
            .OfType<IProgrammaticResourceDeclarationProvider>()
            .ToArray();
        var declarations = scope.ServiceProvider.GetRequiredService<ResourceDeclarationStore>();
        var registrations = scope.ServiceProvider.GetRequiredService<EfCoreResourceStore>();

        foreach (var declaration in declarations.GetDeclarations()
                     .Where(declaration =>
                         declaration.Persistence == ResourceDeclarationPersistence.Persisted))
        {
            var provider = providers.FirstOrDefault(provider =>
                provider.CanApplyDeclaration(declaration))
                ?? throw new InvalidOperationException(
                    $"No provider can apply persisted resource declaration '{declaration.ResourceId}'.");

            provider.ApplyDeclarationAsync(declaration, registrations)
                .GetAwaiter()
                .GetResult();
        }
    }
}
