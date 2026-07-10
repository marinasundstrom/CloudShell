using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using System.Globalization;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationResourceModelGraphDeploymentDescriptor(
    IEnumerable<IAspNetCoreProjectRuntimeEnvironmentProvider>? aspNetCoreEnvironmentProviders = null,
    IEnumerable<IJavaScriptAppRuntimeEnvironmentProvider>? javaScriptEnvironmentProviders = null,
    IEnumerable<IJavaAppRuntimeEnvironmentProvider>? javaEnvironmentProviders = null,
    IEnumerable<IGoAppRuntimeEnvironmentProvider>? goEnvironmentProviders = null,
    IEnumerable<IPythonAppRuntimeEnvironmentProvider>? pythonEnvironmentProviders = null,
    IEnumerable<IDeferredContainerApplicationRuntimeSelector>? deferredRuntimeSelectors = null) :
    IResourceModelGraphDeploymentDescriptor
{
    private const string DefaultOrchestratorId = "default";
    private const string DefaultNetworkName = "cloudshell";
    private readonly IReadOnlyList<IAspNetCoreProjectRuntimeEnvironmentProvider> aspNetCoreEnvironmentProviders =
        aspNetCoreEnvironmentProviders?.ToArray() ?? [];
    private readonly IReadOnlyList<IJavaScriptAppRuntimeEnvironmentProvider> javaScriptEnvironmentProviders =
        javaScriptEnvironmentProviders?.ToArray() ?? [];
    private readonly IReadOnlyList<IJavaAppRuntimeEnvironmentProvider> javaEnvironmentProviders =
        javaEnvironmentProviders?.ToArray() ?? [];
    private readonly IReadOnlyList<IGoAppRuntimeEnvironmentProvider> goEnvironmentProviders =
        goEnvironmentProviders?.ToArray() ?? [];
    private readonly IReadOnlyList<IPythonAppRuntimeEnvironmentProvider> pythonEnvironmentProviders =
        pythonEnvironmentProviders?.ToArray() ?? [];
    private readonly IReadOnlyList<IDeferredContainerApplicationRuntimeSelector> deferredRuntimeSelectors =
        deferredRuntimeSelectors?.ToArray() ?? [];

    public bool CanDescribeDeployment(
        ResourceManagerResource resource,
        Resource graphResource) =>
        graphResource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId &&
        !deferredRuntimeSelectors.Any(selector => selector.IsDeferredRuntimeResource(graphResource));

    public async ValueTask<ResourceOrchestratorDeployment?> DescribeDeploymentAsync(
        ResourceModelGraphDeploymentDescriptionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var container = new ContainerApplicationResource(context.GraphResource);
        var image = container.Image;
        if (string.IsNullOrWhiteSpace(image))
        {
            return null;
        }

        var revisionId = ContainerApplicationRuntimeRevisions.CreateImageRevisionId(
            container.Registry,
            image);
        var workloadKind = string.IsNullOrWhiteSpace(container.BuildContext)
            ? ResourceWorkloadKind.ContainerImage
            : ResourceWorkloadKind.ContainerBuild;
        var service = new ResourceOrchestratorService(
            context.GraphResource.EffectiveResourceId,
            ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(
                context.GraphResource.EffectiveResourceId),
            new ResourceWorkloadConfiguration(
                workloadKind,
                context.GraphResource.Name,
                ProjectPath: container.ProjectPath,
                Image: image.Trim(),
                Registry: string.IsNullOrWhiteSpace(container.Registry)
                    ? ContainerRegistryDefaults.Default
                    : container.Registry.Trim(),
                BuildContext: string.IsNullOrWhiteSpace(container.BuildContext)
                    ? null
                    : container.BuildContext.Trim(),
                Dockerfile: string.IsNullOrWhiteSpace(container.Dockerfile)
                    ? null
                    : container.Dockerfile.Trim(),
                ContainerHostId: container.ContainerHostResourceId,
                Replicas: container.Replicas,
                ReplicasEnabled: container.Replicas > 1,
                EnvironmentVariables: await ResolveEnvironmentVariablesAsync(
                    context.GraphResource,
                    cancellationToken),
                Ports: ToServicePorts(container),
                VolumeMounts: ToVolumeMounts(await container.GetVolumesAsync(cancellationToken))),
            DependsOn: context.GraphResource.State.ResourceDependencyIds,
            Networks: [DefaultNetworkName])
        {
            RuntimeRevisionId = revisionId
        };
        var inputs = CreateDeploymentInputs(container, revisionId);
        var deployment = new ResourceOrchestratorDeployment(
            $"{service.Name}-deployment",
            DefaultOrchestratorId,
            context.GraphResource.EffectiveResourceId,
            service.Name,
            revisionId,
            new ResourceOrchestratorDeploymentSpec(
                service,
                revisionId,
                inputs),
            ToDeploymentStatus(context.Resource.State));
        var definition = deployment.Spec.CreateDeploymentDefinition(deployment.RevisionId);

        return deployment with
        {
            Spec = deployment.Spec with
            {
                Definition = definition
            }
        };
    }

    private static IReadOnlyDictionary<string, string> CreateDeploymentInputs(
        ContainerApplicationResource container,
        string revisionId)
    {
        var replicas = container.Replicas.ToString(CultureInfo.InvariantCulture);
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DeploymentRequestedReplicaSlots] = replicas,
            [ResourceAttributeNames.DeploymentRequestedReplicas] = replicas,
            [ResourceAttributeNames.ContainerRegistry] = string.IsNullOrWhiteSpace(container.Registry)
                ? ContainerRegistryDefaults.Default
                : container.Registry.Trim(),
            [ResourceAttributeNames.RuntimeRevision] = revisionId
        };

        if (!string.IsNullOrWhiteSpace(container.Image))
        {
            inputs[ResourceAttributeNames.ContainerImage] = container.Image.Trim();
        }

        if (!string.IsNullOrWhiteSpace(container.BuildContext))
        {
            inputs[ResourceAttributeNames.ContainerBuildContext] = container.BuildContext.Trim();
        }

        if (!string.IsNullOrWhiteSpace(container.Dockerfile))
        {
            inputs[ResourceAttributeNames.ContainerDockerfile] = container.Dockerfile.Trim();
        }

        return inputs;
    }

    private async ValueTask<IReadOnlyList<EnvironmentVariableAssignment>> ResolveEnvironmentVariablesAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in ContainerizedProjectEnvironmentVariables.Read(resource))
        {
            Add(variables, variable.Name, variable.Value);
        }

        await AddResolvedAsync(variables, aspNetCoreEnvironmentProviders, resource, cancellationToken);
        await AddResolvedAsync(variables, javaScriptEnvironmentProviders, resource, cancellationToken);
        await AddResolvedAsync(variables, javaEnvironmentProviders, resource, cancellationToken);
        await AddResolvedAsync(variables, goEnvironmentProviders, resource, cancellationToken);
        await AddResolvedAsync(variables, pythonEnvironmentProviders, resource, cancellationToken);

        return variables
            .Select(variable => new EnvironmentVariableAssignment(variable.Key, variable.Value))
            .ToArray();
    }

    private static async ValueTask AddResolvedAsync<TProvider>(
        Dictionary<string, string> variables,
        IReadOnlyList<TProvider> providers,
        Resource resource,
        CancellationToken cancellationToken)
        where TProvider : class
    {
        foreach (var provider in providers)
        {
            IReadOnlyDictionary<string, string> resolved = provider switch
            {
                IAspNetCoreProjectRuntimeEnvironmentProvider typed =>
                    await typed.ResolveAsync(resource, cancellationToken),
                IJavaScriptAppRuntimeEnvironmentProvider typed =>
                    await typed.ResolveAsync(resource, cancellationToken),
                IJavaAppRuntimeEnvironmentProvider typed =>
                    await typed.ResolveAsync(resource, cancellationToken),
                IGoAppRuntimeEnvironmentProvider typed =>
                    await typed.ResolveAsync(resource, cancellationToken),
                IPythonAppRuntimeEnvironmentProvider typed =>
                    await typed.ResolveAsync(resource, cancellationToken),
                _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (var (name, value) in resolved)
            {
                Add(variables, name, value);
            }
        }
    }

    private static void Add(
        Dictionary<string, string> variables,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(name) &&
            value is not null)
        {
            variables[name.Trim()] = value;
        }
    }

    private static IReadOnlyList<ServicePort> ToServicePorts(
        ContainerApplicationResource container) =>
        container.EndpointRequests
            .Select(endpoint =>
                new ServicePort(
                    endpoint.Name,
                    endpoint.TargetPort ?? endpoint.Port ?? 80,
                    endpoint.Port,
                    string.IsNullOrWhiteSpace(endpoint.Protocol)
                        ? "tcp"
                        : endpoint.Protocol.Trim(),
                    ParseEnum(endpoint.Exposure, ResourceExposureScope.Local),
                    ParseEnum(endpoint.Assignment, ResourceEndpointAssignment.ProviderDefault),
                    NetworkResourceId: TryGetReferenceResourceId(endpoint.Network),
                    Host: endpoint.Host,
                    IPAddress: endpoint.IpAddress,
                    SessionAffinity: container.SessionAffinity))
            .ToArray();

    private static IReadOnlyList<ResourceVolumeMount> ToVolumeMounts(
        IReadOnlyList<VolumeMountDefinition> mounts) =>
        mounts
            .Select(mount =>
                new ResourceVolumeMount(
                    mount.Volume,
                    mount.TargetPath,
                    mount.ReadOnly))
            .ToArray();

    private static TEnum ParseEnum<TEnum>(
        string? value,
        TEnum fallback)
        where TEnum : struct =>
        !string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed)
                ? parsed
                : fallback;

    private static string? TryGetReferenceResourceId(ResourceReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        return reference.TryGetResourceId(out var resourceId)
                ? resourceId
                : null;
    }

    private static ResourceOrchestratorDeploymentStatus ToDeploymentStatus(
        CloudShell.Abstractions.ResourceManager.ResourceState? state) =>
        state switch
        {
            CloudShell.Abstractions.ResourceManager.ResourceState.Starting or
                CloudShell.Abstractions.ResourceManager.ResourceState.Stopping =>
                    ResourceOrchestratorDeploymentStatus.Applying,
            CloudShell.Abstractions.ResourceManager.ResourceState.Running =>
                ResourceOrchestratorDeploymentStatus.Active,
            CloudShell.Abstractions.ResourceManager.ResourceState.Degraded =>
                ResourceOrchestratorDeploymentStatus.Failed,
            _ => ResourceOrchestratorDeploymentStatus.Pending
        };
}
