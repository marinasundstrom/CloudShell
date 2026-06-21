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
        logger.LogInformation("Starting programmatic resource startup.");
        var result = await startup.StartAutoStartDeclarationsAsync(cancellationToken);

        foreach (var diagnostic in result.Diagnostics)
        {
            LogStartupDiagnostic(logger, diagnostic);
        }

        LogStartupSummary(logger, result);
    }

    private static void LogStartupSummary(
        ILogger logger,
        ResourceDeclarationStartupResult result)
    {
        if (result.HasErrors)
        {
            logger.LogError(
                "Programmatic resource startup completed with {ErrorCount} error(s), {WarningCount} warning(s), and {InformationCount} information diagnostic(s).",
                result.ErrorCount,
                result.WarningCount,
                result.InformationCount);
            return;
        }

        if (result.HasWarnings)
        {
            logger.LogWarning(
                "Programmatic resource startup completed with {WarningCount} warning(s) and {InformationCount} information diagnostic(s).",
                result.WarningCount,
                result.InformationCount);
            return;
        }

        logger.LogInformation(
            "Programmatic resource startup completed successfully with {InformationCount} information diagnostic(s).",
            result.InformationCount);
    }

    private static void LogStartupDiagnostic(
        ILogger logger,
        ResourceDeclarationStartupDiagnostic diagnostic)
    {
        var severity = diagnostic.Severity;
        var resourceName = ResourceDisplayLabels.GetName(diagnostic.ResourceId);
        using var scope = ResourceLogScope.Begin(logger, diagnostic.ResourceId);
        if (diagnostic.IsError)
        {
            logger.LogError(
                "Programmatic resource startup reported {Severity} for resource {ResourceName}: {Message}",
                severity,
                resourceName,
                diagnostic.Message);
            return;
        }

        if (diagnostic.IsWarning)
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
