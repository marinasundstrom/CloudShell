using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceProvider(
    ApplicationResourceStore store) : IResourceProvider, ILogProvider, IResourceProcedureProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, ApplicationProcessState> _processes =
        new(StringComparer.OrdinalIgnoreCase);

    public string Id => "applications";

    public string DisplayName => "Applications";

    public IReadOnlyList<CloudResource> GetResources() => store
        .GetApplications()
        .Select(CreateResource)
        .ToArray();

    public IReadOnlyList<LogDescriptor> GetLogs() => store
        .GetApplications()
        .Select(application => new LogDescriptor(
            GetLogId(application.Id),
            "Application logs",
            DisplayName,
            application.Name,
            LogSourceKind.Resource,
            ResourceId: application.Id,
            SupportsStreaming: true,
            Description: "Combined stdout and stderr from the launched process."))
        .ToArray();

    public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        var applicationId = GetApplicationIdFromLogId(logId);
        if (applicationId is null ||
            !_processes.TryGetValue(applicationId, out var state))
        {
            return Task.FromResult<IReadOnlyList<LogEntry>>([]);
        }

        return Task.FromResult(state.Log.Read(maxEntries, before));
    }

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        DateTimeOffset? lastTimestamp = null;

        foreach (var entry in await ReadLogAsync(logId, initialEntries, cancellationToken: cancellationToken))
        {
            lastTimestamp = entry.Timestamp;
            yield return entry;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            var entries = await ReadLogAsync(logId, 100, cancellationToken: cancellationToken);
            foreach (var entry in entries.Where(entry => lastTimestamp is null || entry.Timestamp > lastTimestamp))
            {
                lastTimestamp = entry.Timestamp;
                yield return entry;
            }
        }
    }

    public async Task SetupApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeDefinition(definition);
        store.Save(normalized);

        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            cancellationToken);
    }

    public async Task UpdateApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeDefinition(definition);
        var existing = store.GetApplication(normalized.Id);
        if (existing is null)
        {
            throw new InvalidOperationException($"Application resource '{normalized.Id}' is not configured.");
        }

        store.Save(normalized);
        await registrations.AssignToGroupAsync(
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            cancellationToken);
    }

    public async Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        await StopApplicationAsync(context.Resource.Id, cancellationToken);
        store.Remove(context.Resource.Id);
        await context.Registrations.RemoveAsync(context.Resource.Id, cancellationToken);
        return ResourceProcedureResult.Completed("Application registration removed.");
    }

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.Resource.EffectiveTypeId, "application.executable", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The application provider cannot execute action '{action.Id}' on resource '{context.Resource.Id}'.");
        }

        switch (action.Kind)
        {
            case ResourceActionKind.Run:
                await StartApplicationAsync(context.Resource.Id, cancellationToken);
                return ResourceProcedureResult.Completed($"Started {context.Resource.Name}.");
            case ResourceActionKind.Stop:
                await StopApplicationAsync(context.Resource.Id, cancellationToken);
                return ResourceProcedureResult.Completed($"Stopped {context.Resource.Name}.");
            case ResourceActionKind.Restart:
                await StopApplicationAsync(context.Resource.Id, cancellationToken);
                await StartApplicationAsync(context.Resource.Id, cancellationToken);
                return ResourceProcedureResult.Completed($"Restarted {context.Resource.Name}.");
            default:
                throw new NotSupportedException(
                    $"Applications do not support action '{action.DisplayName}'.");
        }
    }

    public ApplicationResourceDefinition? GetApplication(string id) => store.GetApplication(id);

    public IReadOnlyList<ApplicationResourceDefinition> GetApplications() => store.GetApplications();

    public bool IsRunning(string applicationId) =>
        _processes.TryGetValue(applicationId, out var state) && !state.Process.HasExited;

    public void Dispose()
    {
        foreach (var state in _processes.Values)
        {
            try
            {
                if (!state.Process.HasExited)
                {
                    state.Process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }

            state.Process.Dispose();
        }
    }

    private async Task StartApplicationAsync(string applicationId, CancellationToken cancellationToken)
    {
        var definition = store.GetApplication(applicationId)
            ?? throw new InvalidOperationException($"Application resource '{applicationId}' is not configured.");

        if (_processes.TryGetValue(definition.Id, out var existing) &&
            !existing.Process.HasExited)
        {
            return;
        }

        var processLog = existing?.Log ?? new ApplicationProcessLog();
        var startInfo = new ProcessStartInfo
        {
            FileName = definition.ExecutablePath,
            Arguments = definition.Arguments ?? string.Empty,
            WorkingDirectory = ResolveWorkingDirectory(definition),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var variable in definition.EnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
            {
                continue;
            }

            startInfo.Environment[variable.Name] = variable.Value;
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) => processLog.Append(args.Data ?? string.Empty, "stdout");
        process.ErrorDataReceived += (_, args) => processLog.Append(args.Data ?? string.Empty, "stderr", "Error");
        process.Exited += (_, _) =>
        {
            processLog.Append(
                $"Process exited with code {process.ExitCode}.",
                "process",
                process.ExitCode == 0 ? "Information" : "Error");
        };

        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        processLog.Append(
            $"Started '{definition.ExecutablePath}' with process id {process.Id}.",
            "process",
            "Information");

        _processes[definition.Id] = new ApplicationProcessState(process, processLog);
        await Task.CompletedTask;
    }

    private async Task StopApplicationAsync(string applicationId, CancellationToken cancellationToken)
    {
        if (!_processes.TryGetValue(applicationId, out var state) ||
            state.Process.HasExited)
        {
            return;
        }

        state.Log.Append("Stopping process.", "process", "Information");
        state.Process.Kill(entireProcessTree: true);
        await state.Process.WaitForExitAsync(cancellationToken);
    }

    private CloudResource CreateResource(ApplicationResourceDefinition application)
    {
        var state = GetState(application.Id);
        return new CloudResource(
            application.Id,
            application.Name,
            "Executable application",
            DisplayName,
            "local",
            state,
            CreateEndpoints(application),
            Path.GetFileName(application.ExecutablePath),
            DateTimeOffset.UtcNow,
            [],
            TypeId: "application.executable",
            Actions: CreateActions(state));
    }

    private ResourceState GetState(string applicationId)
    {
        if (!_processes.TryGetValue(applicationId, out var state))
        {
            return ResourceState.Stopped;
        }

        return state.Process.HasExited
            ? ResourceState.Stopped
            : ResourceState.Running;
    }

    private static IReadOnlyList<ResourceAction> CreateActions(ResourceState state) =>
        state == ResourceState.Running
            ? [ResourceAction.Stop, ResourceAction.Restart]
            : [ResourceAction.Run];

    private static IReadOnlyList<ResourceEndpoint> CreateEndpoints(ApplicationResourceDefinition application)
    {
        if (string.IsNullOrWhiteSpace(application.Endpoint))
        {
            return [new("process", $"process://{application.Id}", "process", false)];
        }

        var endpoint = application.Endpoint;
        var protocol = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? uri.Scheme
            : "tcp";

        return [new("application", endpoint, protocol, true)];
    }

    private static ApplicationResourceDefinition NormalizeDefinition(ApplicationResourceDefinition definition)
    {
        var id = string.IsNullOrWhiteSpace(definition.Id)
            ? CreateId(definition.Name)
            : definition.Id.Trim();

        return definition with
        {
            Id = id,
            Name = definition.Name.Trim(),
            ExecutablePath = definition.ExecutablePath.Trim(),
            Arguments = NormalizeNullable(definition.Arguments),
            WorkingDirectory = NormalizeNullable(definition.WorkingDirectory),
            Endpoint = NormalizeNullable(definition.Endpoint),
            EnvironmentVariables = definition.EnvironmentVariables
                .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
                .Select(variable => variable with { Name = variable.Name.Trim() })
                .ToArray()
        };
    }

    private static string CreateId(string name)
    {
        var slug = SlugPattern()
            .Replace(name.Trim().ToLowerInvariant(), "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug)
            ? $"application:{Guid.NewGuid():N}"
            : $"application:{slug}";
    }

    private static string ResolveWorkingDirectory(ApplicationResourceDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.WorkingDirectory))
        {
            return definition.WorkingDirectory;
        }

        var executableDirectory = Path.GetDirectoryName(definition.ExecutablePath);
        return string.IsNullOrWhiteSpace(executableDirectory)
            ? Environment.CurrentDirectory
            : executableDirectory;
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId;

    private static string GetLogId(string applicationId) => $"{applicationId}:logs";

    private static string? GetApplicationIdFromLogId(string logId) =>
        logId.EndsWith(":logs", StringComparison.OrdinalIgnoreCase)
            ? logId[..^":logs".Length]
            : null;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugPattern();

    private sealed record ApplicationProcessState(
        Process Process,
        ApplicationProcessLog Log);
}
