using System.Globalization;
using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public interface IContainerHostRuntime
{
    Task<ContainerHostOperationResult> EnsureNetworkAsync(
        ContainerHostNetworkSpec network,
        CancellationToken cancellationToken = default);

    Task<ContainerHostOperationResult> RemoveNetworkAsync(
        ContainerHostNetworkSpec network,
        CancellationToken cancellationToken = default);

    Task<ContainerHostOperationResult> RunContainerAsync(
        ContainerHostContainerSpec container,
        CancellationToken cancellationToken = default);

    Task<ContainerHostOperationResult> StartContainerAsync(
        ContainerHostRuntimeHandle handle,
        CancellationToken cancellationToken = default);

    Task<ContainerHostOperationResult> RemoveContainerAsync(
        ContainerHostRuntimeHandle handle,
        CancellationToken cancellationToken = default);

    Task<ContainerHostInspectionResult> InspectContainerAsync(
        ContainerHostRuntimeHandle handle,
        CancellationToken cancellationToken = default);

    Task<ContainerHostOperationResult> ExecuteContainerAsync(
        ContainerHostRuntimeHandle handle,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);

    Task<ContainerHostOperationResult> ReadContainerLogsAsync(
        ContainerHostLogRequest request,
        CancellationToken cancellationToken = default);
}

public static class ContainerHostRuntimeLabels
{
    public const string OwnerResourceId = "cloudshell.owner-resource-id";
}

public sealed record ContainerHostRuntimeHandle(
    string OwnerResourceId,
    string ContainerName);

public sealed record ContainerHostNetworkSpec(
    string OwnerResourceId,
    string Name);

public sealed record ContainerHostPortBinding(
    string HostAddress,
    int HostPort,
    int ContainerPort,
    string Protocol = "tcp");

public sealed record ContainerHostMount(
    string Source,
    string Target,
    bool ReadOnly = false);

public sealed record ContainerHostContainerSpec(
    string OwnerResourceId,
    string Name,
    string Image,
    string? NetworkName = null,
    IReadOnlyList<string>? NetworkAliases = null,
    IReadOnlyList<ContainerHostPortBinding>? PublishedPorts = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    IReadOnlyDictionary<string, string>? Labels = null,
    IReadOnlyList<ContainerHostMount>? Mounts = null,
    IReadOnlyList<string>? Arguments = null,
    bool Detach = true,
    bool RemoveWhenStopped = true);

public sealed record ContainerHostLogRequest(
    string OwnerResourceId,
    string ContainerName,
    int Tail = 100,
    DateTimeOffset? Before = null,
    bool IncludeTimestamps = true);

public sealed record ContainerHostOperationResult(
    int ExitCode,
    string Output,
    string Error)
{
    public bool Succeeded => ExitCode == 0;
}

public enum ContainerHostContainerState
{
    Unknown,
    Created,
    Running,
    Stopped
}

public sealed record ContainerHostNetworkAttachment(
    string NetworkName,
    string? IPv4Address,
    string? IPv6Address = null,
    string? HostName = null);

public sealed record ContainerHostContainerObservation(
    string Id,
    ContainerHostContainerState State,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, ContainerHostNetworkAttachment> Networks);

public sealed record ContainerHostInspectionResult(
    ContainerHostContainerObservation? Container,
    string? Error = null)
{
    public bool Found => Container is not null;
}

public sealed class CommandContainerHostRuntime(
    ILocalContainerApplicationCommandRunner commandRunner) : IContainerHostRuntime
{
    public async Task<ContainerHostOperationResult> EnsureNetworkAsync(
        ContainerHostNetworkSpec network,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(network.OwnerResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(network.Name);

        var inspection = await RunAsync(
            ["network", "inspect", network.Name],
            cancellationToken);
        return inspection.Succeeded
            ? inspection
            : await RunAsync(["network", "create", network.Name], cancellationToken);
    }

    public Task<ContainerHostOperationResult> RemoveNetworkAsync(
        ContainerHostNetworkSpec network,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(network.OwnerResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(network.Name);
        return RunAsync(["network", "rm", network.Name], cancellationToken);
    }

    public Task<ContainerHostOperationResult> RunContainerAsync(
        ContainerHostContainerSpec container,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container.OwnerResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(container.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(container.Image);

        var arguments = new List<string> { "run" };
        if (container.Detach)
        {
            arguments.Add("--detach");
        }

        arguments.Add("--name");
        arguments.Add(container.Name);
        if (container.RemoveWhenStopped)
        {
            arguments.Add("--rm");
        }

        if (!string.IsNullOrWhiteSpace(container.NetworkName))
        {
            arguments.Add("--network");
            arguments.Add(container.NetworkName);
        }

        foreach (var alias in container.NetworkAliases ?? [])
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                arguments.Add("--network-alias");
                arguments.Add(alias);
            }
        }

        foreach (var binding in container.PublishedPorts ?? [])
        {
            arguments.Add("--publish");
            arguments.Add(CreatePortBinding(binding));
        }

        foreach (var variable in container.EnvironmentVariables ?? new Dictionary<string, string>())
        {
            arguments.Add("--env");
            arguments.Add($"{variable.Key}={variable.Value}");
        }

        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ContainerHostRuntimeLabels.OwnerResourceId] = container.OwnerResourceId
        };
        foreach (var label in container.Labels ?? new Dictionary<string, string>())
        {
            if (!string.Equals(
                    label.Key,
                    ContainerHostRuntimeLabels.OwnerResourceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                labels[label.Key] = label.Value;
            }
        }

        foreach (var label in labels)
        {
            arguments.Add("--label");
            arguments.Add($"{label.Key}={label.Value}");
        }

        foreach (var mount in container.Mounts ?? [])
        {
            arguments.Add("--volume");
            arguments.Add($"{mount.Source}:{mount.Target}{(mount.ReadOnly ? ":ro" : string.Empty)}");
        }

        arguments.Add(container.Image);
        arguments.AddRange(container.Arguments ?? []);
        return RunAsync(arguments, cancellationToken);
    }

    public Task<ContainerHostOperationResult> StartContainerAsync(
        ContainerHostRuntimeHandle handle,
        CancellationToken cancellationToken = default)
    {
        ValidateHandle(handle);
        return RunAsync(["start", handle.ContainerName], cancellationToken);
    }

    public Task<ContainerHostOperationResult> RemoveContainerAsync(
        ContainerHostRuntimeHandle handle,
        CancellationToken cancellationToken = default)
    {
        ValidateHandle(handle);
        return RunAsync(["rm", "-f", handle.ContainerName], cancellationToken);
    }

    public async Task<ContainerHostInspectionResult> InspectContainerAsync(
        ContainerHostRuntimeHandle handle,
        CancellationToken cancellationToken = default)
    {
        ValidateHandle(handle);
        var result = await RunAsync(
            ["container", "inspect", handle.ContainerName],
            cancellationToken);
        if (!result.Succeeded)
        {
            return new(null, FirstNonEmpty(result.Error, result.Output));
        }

        if (!TryParseInspection(result.Output, out var observation))
        {
            return new(null, "The container host returned an unsupported inspection payload.");
        }

        if (!observation.Labels.TryGetValue(ContainerHostRuntimeLabels.OwnerResourceId, out var ownerResourceId) ||
            !string.Equals(ownerResourceId, handle.OwnerResourceId, StringComparison.OrdinalIgnoreCase))
        {
            return new(
                null,
                $"Container '{handle.ContainerName}' is not owned by CloudShell resource '{handle.OwnerResourceId}'.");
        }

        return new(observation);
    }

    public Task<ContainerHostOperationResult> ExecuteContainerAsync(
        ContainerHostRuntimeHandle handle,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ValidateHandle(handle);
        ArgumentNullException.ThrowIfNull(arguments);
        return RunAsync(["exec", handle.ContainerName, .. arguments], cancellationToken);
    }

    public Task<ContainerHostOperationResult> ReadContainerLogsAsync(
        ContainerHostLogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwnerResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerName);
        var arguments = new List<string> { "logs" };
        if (request.IncludeTimestamps)
        {
            arguments.Add("--timestamps");
        }

        arguments.Add("--tail");
        arguments.Add(Math.Max(1, request.Tail).ToString(CultureInfo.InvariantCulture));
        if (request.Before is { } before)
        {
            arguments.Add("--until");
            arguments.Add(before.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }

        arguments.Add(request.ContainerName);
        return RunAsync(arguments, cancellationToken);
    }

    private async Task<ContainerHostOperationResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(
            "docker",
            arguments,
            cancellationToken,
            throwOnError: false);
        return new(result.ExitCode, result.Output, result.Error);
    }

    private static void ValidateHandle(ContainerHostRuntimeHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(handle.OwnerResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(handle.ContainerName);
    }

    internal static bool TryParseInspection(
        string json,
        out ContainerHostContainerObservation observation)
    {
        observation = null!;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() == 0)
            {
                return false;
            }

            var root = document.RootElement[0];
            return TryParseAppleInspection(root, out observation) ||
                TryParseDockerCompatibleInspection(root, out observation);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseAppleInspection(
        JsonElement root,
        out ContainerHostContainerObservation observation)
    {
        observation = null!;
        if (!TryGetProperty(root, "configuration", out var configuration) ||
            !TryGetProperty(root, "status", out var status))
        {
            return false;
        }

        var id = GetString(configuration, "id") ?? GetString(root, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var labels = ReadStringDictionary(configuration, "labels");
        var networks = new Dictionary<string, ContainerHostNetworkAttachment>(StringComparer.OrdinalIgnoreCase);
        if (TryGetProperty(status, "networks", out var networkArray) &&
            networkArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var network in networkArray.EnumerateArray())
            {
                var name = GetString(network, "network");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                networks[name] = new(
                    name,
                    RemovePrefixLength(GetString(network, "ipv4Address")),
                    RemovePrefixLength(GetString(network, "ipv6Address")),
                    GetString(network, "hostname"));
            }
        }

        observation = new(
            id,
            ParseState(GetString(status, "state")),
            labels,
            networks);
        return true;
    }

    private static bool TryParseDockerCompatibleInspection(
        JsonElement root,
        out ContainerHostContainerObservation observation)
    {
        observation = null!;
        if (!TryGetProperty(root, "State", out var state))
        {
            return false;
        }

        var id = GetString(root, "Name")?.TrimStart('/') ?? GetString(root, "Id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var labels = TryGetProperty(root, "Config", out var configuration)
            ? ReadStringDictionary(configuration, "Labels")
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var networks = new Dictionary<string, ContainerHostNetworkAttachment>(StringComparer.OrdinalIgnoreCase);
        if (TryGetProperty(root, "NetworkSettings", out var networkSettings) &&
            TryGetProperty(networkSettings, "Networks", out var networkObject) &&
            networkObject.ValueKind == JsonValueKind.Object)
        {
            foreach (var network in networkObject.EnumerateObject())
            {
                networks[network.Name] = new(
                    network.Name,
                    GetString(network.Value, "IPAddress"),
                    GetString(network.Value, "GlobalIPv6Address"),
                    GetString(root, "Config", "Hostname"));
            }
        }

        observation = new(
            id,
            ParseState(GetString(state, "Status")),
            labels,
            networks);
        return true;
    }

    private static IReadOnlyDictionary<string, string> ReadStringDictionary(
        JsonElement parent,
        string propertyName)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(parent, propertyName, out var dictionary) ||
            dictionary.ValueKind != JsonValueKind.Object)
        {
            return values;
        }

        foreach (var property in dictionary.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                values[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        return values;
    }

    private static ContainerHostContainerState ParseState(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "created" => ContainerHostContainerState.Created,
            "running" => ContainerHostContainerState.Running,
            "exited" or "dead" or "stopped" => ContainerHostContainerState.Stopped,
            _ => ContainerHostContainerState.Unknown
        };

    private static string CreatePortBinding(ContainerHostPortBinding binding)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binding.HostPort);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binding.ContainerPort);
        var protocol = string.IsNullOrWhiteSpace(binding.Protocol)
            ? "tcp"
            : binding.Protocol.Trim().ToLowerInvariant();
        return $"{binding.HostAddress}:{binding.HostPort.ToString(CultureInfo.InvariantCulture)}:{binding.ContainerPort.ToString(CultureInfo.InvariantCulture)}/{protocol}";
    }

    private static string? RemovePrefixLength(string? address) =>
        string.IsNullOrWhiteSpace(address)
            ? null
            : address.Split('/', 2)[0];

    private static string? GetString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? GetString(
        JsonElement element,
        string parentPropertyName,
        string propertyName) =>
        TryGetProperty(element, parentPropertyName, out var parent)
            ? GetString(parent, propertyName)
            : null;

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in element.EnumerateObject())
            {
                if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
