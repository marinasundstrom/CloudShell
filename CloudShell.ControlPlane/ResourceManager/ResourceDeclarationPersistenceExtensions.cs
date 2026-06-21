using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudShell.ControlPlane.ResourceManager;

public static class ResourceDeclarationPersistenceExtensions
{
    public static void ApplyPersistedProgrammaticResourceDeclarations(this IServiceProvider services)
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

    public static async Task StartProgrammaticResourceDeclarationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(CloudShellLogCategories.ProgrammaticResourceStartup);
        var startup = scope.ServiceProvider.GetRequiredService<ResourceDeclarationStartupService>();
        var result = await startup.StartAutoStartDeclarationsAsync(cancellationToken);

        foreach (var diagnostic in result.Diagnostics)
        {
            LogStartupDiagnostic(logger, diagnostic);
        }
    }

    private static void LogStartupDiagnostic(
        ILogger logger,
        ResourceDeclarationStartupDiagnostic diagnostic)
    {
        var severity = diagnostic.Severity;
        var resourceName = ResourceDisplayLabels.GetName(diagnostic.ResourceId);
        using var scope = ResourceLogScope.Begin(logger, diagnostic.ResourceId);
        if (string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError(
                "Programmatic resource startup reported {Severity} for resource {ResourceName}: {Message}",
                severity,
                resourceName,
                diagnostic.Message);
            return;
        }

        if (string.Equals(severity, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Programmatic resource startup reported {Severity} for resource {ResourceName}: {Message}",
                severity,
                resourceName,
                diagnostic.Message);
            return;
        }

        logger.LogInformation(
            "Programmatic resource startup reported {Severity} for resource {ResourceName}: {Message}",
            severity,
            resourceName,
            diagnostic.Message);
    }
}
