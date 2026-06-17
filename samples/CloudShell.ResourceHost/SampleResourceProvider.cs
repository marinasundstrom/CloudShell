using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ResourceHost;

public sealed class SampleResourceProvider : IResourceProvider, IResourceProcedureProvider
{
    public const string ProviderId = "sample.resources";

    private readonly object gate = new();
    private readonly HashSet<string> deleted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResourceState> states = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sample:api"] = ResourceState.Running,
        ["sample:database"] = ResourceState.Running,
        ["sample:worker"] = ResourceState.Stopped
    };

    public string Id => ProviderId;

    public string DisplayName => "Sample Resources";

    public IReadOnlyList<Resource> GetResources()
    {
        Resource[] resources =
        [
            Create(
                "sample:api",
                "Sample API",
                ResourceState.Running,
                "https://api.sample.local",
                dependsOn: ["sample:database"]),
            Create(
                "sample:database",
                "Sample Database",
                ResourceState.Running,
                "tcp://sample-db:5432"),
            Create(
                "sample:worker",
                "Sample Worker",
                ResourceState.Stopped,
                "queue://sample-work")
        ];

        return resources.Where(resource => !IsDeleted(resource.Id)).ToArray();
    }

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            states.Remove(context.Resource.Id);
            deleted.Add(context.Resource.Id);
        }

        return Task.FromResult(ResourceProcedureResult.Completed($"Deleted {context.Resource.Name}."));
    }

    public Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        var state = action.Kind switch
        {
            ResourceActionKind.Start or ResourceActionKind.Restart => ResourceState.Running,
            ResourceActionKind.Stop => ResourceState.Stopped,
            ResourceActionKind.Pause => ResourceState.Paused,
            _ => context.Resource.State ?? ResourceState.Unknown
        };

        lock (gate)
        {
            states[context.Resource.Id] = state;
            deleted.Remove(context.Resource.Id);
        }

        return Task.FromResult(ResourceProcedureResult.Completed(
            $"{action.DisplayName} completed for {context.Resource.Name}."));
    }

    private Resource Create(
        string id,
        string name,
        ResourceState state,
        string endpoint,
        IReadOnlyList<string>? dependsOn = null)
    {
        lock (gate)
        {
            state = states.TryGetValue(id, out var currentState) ? currentState : state;
        }

        return new(
            id,
            name,
            "Sample service",
            DisplayName,
            "local",
            state,
            [ResourceEndpoint.Contract("default", GetProtocol(endpoint), ResourceExposureScope.Public, GetPort(endpoint))],
            "0.1.0",
            DateTimeOffset.UtcNow,
            dependsOn ?? [],
            TypeId: "sample-service",
            Actions: CreateActions(state),
            EndpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    id,
                    "default",
                    endpoint,
                    ResourceExposureScope.Public,
                    sourceEndpointName: "default")
            ]);
    }

    private static string GetProtocol(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? uri.Scheme
            : endpoint.Split(':', 2)[0];

    private static int? GetPort(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Port > 0
            ? uri.Port
            : null;

    private static IReadOnlyList<ResourceAction> CreateActions(ResourceState state) =>
        state == ResourceState.Running
            ? [ResourceAction.Stop, ResourceAction.Restart]
            : [ResourceAction.Start];

    private bool IsDeleted(string id)
    {
        lock (gate)
        {
            return deleted.Contains(id);
        }
    }
}
