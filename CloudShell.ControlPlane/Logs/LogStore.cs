using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Logs;

public sealed class LogStore(
    IEnumerable<ILogProvider> providers,
    IResourceManagerStore resourceManager,
    CloudShellExtensionRegistry extensionRegistry,
    ICloudShellExtensionActivationStore activationStore,
    IEnumerable<ILogSourceContributor>? sourceContributors = null) : ILogStore
{
    private readonly IReadOnlyList<ILogProvider> providers = providers.ToArray();
    private readonly IReadOnlyList<ILogSourceContributor> sourceContributors =
        sourceContributors?.ToArray() ?? [];

    public IReadOnlyList<ILogProvider> Providers => providers
        .Where(IsProviderActive)
        .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<LogSource> GetLogSources()
        => CreateSourceCatalog().GetLogSources();

    public LogSource? GetLogSource(string logSourceId) =>
        CreateSourceCatalog().GetLogSource(logSourceId);

    private async ValueTask<ILogSourceSession?> OpenLogSourceSessionAsync(
        string logSourceId,
        CancellationToken cancellationToken = default)
    {
        var source = GetLogSource(logSourceId);
        if (source is null)
        {
            return null;
        }

        foreach (var provider in Providers)
        {
            if (!provider.CanOpenLogSource(source))
            {
                continue;
            }

            var session = await provider.OpenLogSourceAsync(source, cancellationToken);
            if (session is not null)
            {
                return session;
            }
        }

        return null;
    }

    public async ValueTask<ILogSession?> OpenLogSessionAsync(
        IReadOnlyList<string> logSourceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logSourceIds);
        var sourceIds = logSourceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceIds.Length == 0)
        {
            return null;
        }

        var opened = new List<CompositeLogSession.SourceSession>(sourceIds.Length);
        try
        {
            foreach (var sourceId in sourceIds)
            {
                var source = GetLogSource(sourceId);
                if (source is null || !source.SupportsReading)
                {
                    foreach (var openedSource in opened)
                    {
                        await openedSource.Session.DisposeAsync();
                    }

                    return null;
                }

                var session = await OpenLogSourceSessionAsync(sourceId, cancellationToken);
                if (session is null)
                {
                    foreach (var openedSource in opened)
                    {
                        await openedSource.Session.DisposeAsync();
                    }

                    return null;
                }

                opened.Add(new CompositeLogSession.SourceSession(source, session));
            }

            return new CompositeLogSession(opened);
        }
        catch
        {
            foreach (var openedSource in opened)
            {
                await openedSource.Session.DisposeAsync();
            }

            throw;
        }
    }

    private bool IsProviderActive(ILogProvider provider)
    {
        var extensionProviderTypes = extensionRegistry
            .Extensions
            .SelectMany(extension => extension.LogProviderTypes)
            .ToArray();
        var providerType = provider.GetType();
        var isExtensionProvider = extensionProviderTypes.Any(type => type.IsAssignableFrom(providerType));
        if (!isExtensionProvider)
        {
            return true;
        }

        return extensionRegistry
            .GetActiveExtensions(activationStore)
            .SelectMany(extension => extension.LogProviderTypes)
            .Any(type => type.IsAssignableFrom(providerType));
    }

    private ILogSourceCatalog CreateSourceCatalog() =>
        new LogSourceCatalog(resourceManager, GetSourceContributors());

    private IReadOnlyList<ILogSourceContributor> GetSourceContributors()
    {
        var activeProviders = Providers;
        return activeProviders
            .Cast<ILogSourceContributor>()
            .Concat(sourceContributors
                .Where(IsSourceContributorActive)
                .Where(contributor =>
                    activeProviders.All(provider => !ReferenceEquals(provider, contributor))))
            .ToArray();
    }

    private bool IsSourceContributorActive(ILogSourceContributor contributor)
    {
        var extensionContributorTypes = extensionRegistry
            .Extensions
            .SelectMany(extension => extension.LogSourceContributorTypes)
            .ToArray();
        var contributorType = contributor.GetType();
        var isExtensionContributor = extensionContributorTypes.Any(type => type.IsAssignableFrom(contributorType));
        if (!isExtensionContributor)
        {
            return true;
        }

        return extensionRegistry
            .GetActiveExtensions(activationStore)
            .SelectMany(extension => extension.LogSourceContributorTypes)
            .Any(type => type.IsAssignableFrom(contributorType));
    }
}
