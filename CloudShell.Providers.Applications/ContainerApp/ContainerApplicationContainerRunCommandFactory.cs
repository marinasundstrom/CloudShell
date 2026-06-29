using System.Diagnostics;
using System.Globalization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationContainerRunCommandFactory(ApplicationProviderOptions options)
{
    private readonly ApplicationResourcePortResolver _ports = new(options);

    public ProcessStartInfo CreateStartInfo(
        ContainerHostDescriptor engine,
        ApplicationResourceDefinition definition,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        IReadOnlyList<ServicePort> runtimeProbePorts,
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables,
        IReadOnlyList<ApplicationResourceVolumeMounts.LocalContainerVolumeMaterialization> volumeMaterializations,
        string imageReference,
        string? imagePlatform,
        bool useIngress)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ApplicationContainerHostCommands.GetExecutable(engine),
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        ApplicationContainerHostCommands.ConfigureEnvironment(startInfo, engine);
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--name");
        startInfo.ArgumentList.Add(instance.Name);
        if (definition.Lifetime == ApplicationLifetime.ControlPlaneScoped)
        {
            startInfo.ArgumentList.Add("--rm");
        }

        var network = service.ServiceNetworks.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        if (!string.IsNullOrWhiteSpace(network))
        {
            startInfo.ArgumentList.Add("--network");
            startInfo.ArgumentList.Add(network);
            if (useIngress)
            {
                startInfo.ArgumentList.Add("--network-alias");
                startInfo.ArgumentList.Add(ContainerApplicationIngressOperations.CreateIngressTargetName(service, instance));
            }
        }

        if (instance.ReplicaOrdinal == 1)
        {
            foreach (var port in service.ServicePorts.Where(port => !useIngress || !ContainerApplicationIngressOperations.IsIngressPort(port)))
            {
                AddPortMapping(
                    startInfo,
                    _ports.ResolveLocalPort(definition.Id, port),
                    port.TargetPort,
                    port.Protocol);
            }
        }

        foreach (var port in runtimeProbePorts)
        {
            AddPortMapping(
                startInfo,
                _ports.ResolveReplicaProbeLocalPort(definition.Id, port, instance),
                port.TargetPort,
                port.Protocol);
        }

        foreach (var variable in environmentVariables)
        {
            AddEnvironmentVariable(startInfo, variable.Name, variable.Value);
        }

        AddEnvironmentVariable(startInfo, "CLOUDSHELL_RESOURCE_ID", definition.Id);
        AddEnvironmentVariable(
            startInfo,
            "CLOUDSHELL_REPLICA_ORDINAL",
            instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture));

        foreach (var volumeMaterialization in volumeMaterializations)
        {
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add(volumeMaterialization.Argument);
        }

        if (!string.IsNullOrWhiteSpace(imagePlatform))
        {
            startInfo.ArgumentList.Add("--platform");
            startInfo.ArgumentList.Add(imagePlatform);
        }

        startInfo.ArgumentList.Add(imageReference);
        return startInfo;
    }

    public static string NormalizeContainerPublishProtocol(string? protocol) =>
        NormalizeProtocol(protocol) switch
        {
            "http" or "https" => "tcp",
            "udp" => "udp",
            "sctp" => "sctp",
            _ => "tcp"
        };

    private static void AddPortMapping(
        ProcessStartInfo startInfo,
        int hostPort,
        int targetPort,
        string protocol)
    {
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add($"{hostPort}:{targetPort}/{NormalizeContainerPublishProtocol(protocol)}");
    }

    private static void AddEnvironmentVariable(
        ProcessStartInfo startInfo,
        string name,
        string value)
    {
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add($"{name}={value}");
    }

    private static string NormalizeProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol.Trim().ToLowerInvariant();
}
