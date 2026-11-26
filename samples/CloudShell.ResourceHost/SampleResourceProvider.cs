using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;

namespace CloudShell.ResourceHost;

public sealed class SampleResourceProvider : IResourceProvider, IResourceProcedureProvider
{
    private readonly bool designPreview;
    public SampleResourceProvider(IConfiguration configuration) => designPreview = configuration.GetValue<bool>("DesignPreview:Enabled");

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

        if (designPreview)
        {
            resources = [..resources,
                Create("sample:sql", "SQL Server (simulated)", ResourceState.Running, "tcp://sql.example.test:1433", kind: "SQL server", typeId: SqlServerResourceTypeProvider.ResourceTypeId.ToString()),
                Create("sample:orders-db", "Orders", ResourceState.Running, "sql://sql.example.test/orders", kind: "Database", typeId: SqlDatabaseResourceTypeProvider.ResourceTypeId.ToString(), parentId: "sample:sql"),
                Create("sample:reporting-db", "Reporting", ResourceState.Stopped, "sql://sql.example.test/reporting", kind: "Database", typeId: SqlDatabaseResourceTypeProvider.ResourceTypeId.ToString(), parentId: "sample:sql"),
                Create("sample:archive-db", "HistoricalOrdersArchiveForInternationalFulfillmentAndReturns", ResourceState.Running, "sql://sql.example.test/archive", kind: "Database", typeId: SqlDatabaseResourceTypeProvider.ResourceTypeId.ToString(), parentId: "sample:sql"),
                Create("sample:analytics-db", "Analytics", ResourceState.Degraded, "sql://sql.example.test/analytics", kind: "Database", typeId: SqlDatabaseResourceTypeProvider.ResourceTypeId.ToString(), parentId: "sample:sql"),
                Create("sample:audit-db", "Audit", ResourceState.Running, "sql://sql.example.test/audit", kind: "Database", typeId: SqlDatabaseResourceTypeProvider.ResourceTypeId.ToString(), parentId: "sample:sql"),
                Create("sample:warehouse-db", "Warehouse", ResourceState.Running, "sql://sql.example.test/warehouse", kind: "Database", typeId: SqlDatabaseResourceTypeProvider.ResourceTypeId.ToString(), parentId: "sample:sql"),
                Create("sample:unknown-db", "Unreported", ResourceState.Unknown, "sql://sql.example.test/unknown", kind: "Database", typeId: SqlDatabaseResourceTypeProvider.ResourceTypeId.ToString(), parentId: "sample:sql"),
                Create("sample:broker", "RabbitMQ (simulated)", ResourceState.Running, "amqp://broker.example.test:5672", kind: "Message broker", typeId: RabbitMQResourceTypeProvider.ResourceTypeId.ToString(), endpointName: "amqp"),
                Create("sample:orders-queue", "Orders awaiting fulfillment", ResourceState.Running, "queue://orders", kind: "Queue", typeId: "sample-queue", parentId: "sample:broker"),
                Create("sample:deadletters", "Orders awaiting fulfillment / dead letters", ResourceState.Stopped, "queue://orders-deadletters", kind: "Queue", typeId: "sample-queue", parentId: "sample:orders-queue")];
        }
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
        IReadOnlyList<string>? dependsOn = null,
        string kind = "Sample service", string typeId = "sample-service", string? parentId = null, string endpointName = "default")
    {
        lock (gate)
        {
            state = states.TryGetValue(id, out var currentState) ? currentState : state;
        }

        return new(
            id,
            name,
            kind,
            DisplayName,
            "local",
            state,
            [ResourceEndpoint.Contract(endpointName, GetProtocol(endpoint), ResourceExposureScope.Public, GetPort(endpoint))],
            "0.1.0",
            DateTimeOffset.UtcNow,
            dependsOn ?? [],
            TypeId: typeId,
            ParentResourceId: parentId,
            Actions: CreateActions(state),
            EndpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    id,
                    endpointName,
                    endpoint,
                    ResourceExposureScope.Public,
                    sourceEndpointName: endpointName)
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
