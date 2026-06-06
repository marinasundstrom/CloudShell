using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceProvider(
    ApplicationResourceStore store,
    ApplicationRuntimeStateStore runtimeStates,
    ApplicationProviderOptions options,
    IHostEnvironment environment) :
    IResourceProvider,
    ILogProvider,
    IResourceProcedureProvider,
    IResourceTemplateProvider,
    IDisposable
{
    private static readonly JsonSerializerOptions TemplateSerializerOptions = new(JsonSerializerDefaults.Web);

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
            Description: "Application stdout, stderr, and lifecycle events."))
        .ToArray();

    public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        var applicationId = GetApplicationIdFromLogId(logId);
        if (applicationId is null)
        {
            return Task.FromResult<IReadOnlyList<LogEntry>>([]);
        }

        var log = GetProcessLog(applicationId);
        return Task.FromResult(log.Read(maxEntries, before));
    }

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var applicationId = GetApplicationIdFromLogId(logId);
        if (applicationId is null)
        {
            yield break;
        }

        var log = GetProcessLog(applicationId);
        foreach (var entry in log.Read(initialEntries, before: null))
        {
            yield return entry;
        }

        var seenEntries = log.CountEntries();
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            var entries = log.ReadAfter(seenEntries);
            seenEntries += entries.Count;
            foreach (var entry in entries)
            {
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
        await StopApplicationAsync(context.Resource.Id, force: true, cancellationToken);
        store.Remove(context.Resource.Id);
        runtimeStates.Remove(context.Resource.Id);
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
                await StopApplicationAsync(context.Resource.Id, force: true, cancellationToken);
                return ResourceProcedureResult.Completed($"Stopped {context.Resource.Name}.");
            case ResourceActionKind.Restart:
                await StopApplicationAsync(context.Resource.Id, force: true, cancellationToken);
                await StartApplicationAsync(context.Resource.Id, cancellationToken);
                return ResourceProcedureResult.Completed($"Restarted {context.Resource.Name}.");
            default:
                throw new NotSupportedException(
                    $"Applications do not support action '{action.DisplayName}'.");
        }
    }

    public bool CanExport(CloudResource resource) =>
        string.Equals(resource.EffectiveTypeId, "application.executable", StringComparison.OrdinalIgnoreCase) &&
        store.GetApplication(resource.Id) is not null;

    public Task<ResourceTemplateDefinition> ExportAsync(
        CloudResource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default)
    {
        var application = store.GetApplication(resource.Id)
            ?? throw new InvalidOperationException($"Application resource '{resource.Id}' is not configured.");

        var configuration = new ApplicationResourceTemplateConfiguration(
            application.ExecutablePath,
            application.Arguments,
            application.WorkingDirectory,
            application.Endpoint,
            application.EnvironmentVariables,
            application.Lifetime);

        return Task.FromResult(new ResourceTemplateDefinition(
            application.Name,
            Id,
            "application.executable",
            resource.DependsOn,
            "1.0",
            JsonSerializer.SerializeToElement(configuration, TemplateSerializerOptions)));
    }

    public bool CanImport(ResourceTemplateDefinition template) =>
        string.Equals(template.ProviderId, Id, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(template.ResourceType, "application.executable", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(template.ProviderConfigurationVersion, "1.0", StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default)
    {
        if (!CanImport(template))
        {
            throw new InvalidOperationException("The application resource template is not supported.");
        }

        var configuration = template.Configuration.Deserialize<ApplicationResourceTemplateConfiguration>(
            TemplateSerializerOptions)
            ?? throw new InvalidOperationException("The application resource template configuration is invalid.");

        var resourceId = CreateUniqueImportId(template.Name);
        var definition = new ApplicationResourceDefinition(
            resourceId,
            template.Name,
            configuration.ExecutablePath,
            configuration.Arguments,
            configuration.WorkingDirectory,
            configuration.Endpoint,
            configuration.EnvironmentVariables,
            configuration.Lifetime);

        await SetupApplicationAsync(
            definition,
            context.ResourceGroupId,
            context.Registrations,
            cancellationToken);

        return new ResourceTemplateImportResult(
            resourceId,
            $"Imported application resource '{template.Name}'.");
    }

    public ApplicationResourceDefinition? GetApplication(string id) => store.GetApplication(id);

    public IReadOnlyList<ApplicationResourceDefinition> GetApplications() => store.GetApplications();

    public bool IsRunning(string applicationId) =>
        TryGetRunningProcess(
            store.GetApplication(applicationId),
            out _);

    public void Dispose()
    {
        foreach (var (applicationId, state) in _processes)
        {
            try
            {
                if (state.Lifetime == ApplicationLifetime.ControlPlaneScoped &&
                    !state.Process.HasExited)
                {
                    state.Process.Kill(entireProcessTree: true);
                    runtimeStates.Save(new ApplicationRuntimeState(
                        applicationId,
                        state.Process.Id,
                        null,
                        DateTimeOffset.UtcNow,
                        TryGetExitCode(state.Process),
                        state.LogPath));
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

        if (TryGetRunningProcess(definition, out _))
        {
            return;
        }

        var logPath = GetLogPath(definition.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var processLog = new ApplicationProcessLog(logPath);
        var startInfo = definition.Lifetime == ApplicationLifetime.Detached
            ? CreateDetachedStartInfo(definition, logPath)
            : CreateScopedStartInfo(definition);

        foreach (var variable in definition.EnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
            {
                continue;
            }

            startInfo.Environment[variable.Name] = variable.Value;
        }
        startInfo.Environment["CLOUDSHELL_RESOURCE_ID"] = definition.Id;

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (definition.Lifetime == ApplicationLifetime.ControlPlaneScoped)
        {
            process.OutputDataReceived += (_, args) => processLog.Append(args.Data ?? string.Empty, "stdout");
            process.ErrorDataReceived += (_, args) => processLog.Append(args.Data ?? string.Empty, "stderr", "Error");
        }

        process.Exited += (_, _) =>
        {
            processLog.Append(
                $"Process exited with code {process.ExitCode}.",
                "process",
                process.ExitCode == 0 ? "Information" : "Error");
            runtimeStates.Save(new ApplicationRuntimeState(
                definition.Id,
                process.Id,
                null,
                DateTimeOffset.UtcNow,
                process.ExitCode,
                logPath));
        };

        cancellationToken.ThrowIfCancellationRequested();
        process.Start();

        if (definition.Lifetime == ApplicationLifetime.ControlPlaneScoped)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        var startedAt = TryGetStartTime(process);
        runtimeStates.Save(new ApplicationRuntimeState(
            definition.Id,
            process.Id,
            startedAt,
            DateTimeOffset.UtcNow,
            LogPath: logPath));

        processLog.Append(
            $"Started '{definition.ExecutablePath}' with process id {process.Id} using {definition.Lifetime} lifetime.",
            "process",
            "Information");

        _processes[definition.Id] = new ApplicationProcessState(
            process,
            processLog,
            definition.Lifetime,
            logPath);
        await Task.CompletedTask;
    }

    private async Task StopApplicationAsync(
        string applicationId,
        bool force,
        CancellationToken cancellationToken)
    {
        var application = store.GetApplication(applicationId);
        var log = GetProcessLog(applicationId);

        if (!TryGetRunningProcess(application, out var process))
        {
            return;
        }

        log.Append(force ? "Stopping process." : "Stopping control-plane-scoped process.", "process", "Information");
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(cancellationToken);
        runtimeStates.Save(new ApplicationRuntimeState(
            applicationId,
            process.Id,
            null,
            DateTimeOffset.UtcNow,
            TryGetExitCode(process),
            GetLogPath(applicationId)));
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
        return TryGetRunningProcess(
            store.GetApplication(applicationId),
            out _)
            ? ResourceState.Running
            : ResourceState.Stopped;
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

    private static ProcessStartInfo CreateScopedStartInfo(ApplicationResourceDefinition definition) =>
        new()
        {
            FileName = definition.ExecutablePath,
            Arguments = definition.Arguments ?? string.Empty,
            WorkingDirectory = ResolveWorkingDirectory(definition),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

    private static ProcessStartInfo CreateDetachedStartInfo(
        ApplicationResourceDefinition definition,
        string logPath)
    {
        var workingDirectory = ResolveWorkingDirectory(definition);
        var arguments = definition.Arguments ?? string.Empty;

        if (OperatingSystem.IsWindows())
        {
            var command = $"\"{EscapeWindowsCommandArgument(definition.ExecutablePath)}\" {arguments} >> \"{EscapeWindowsCommandArgument(logPath)}\" 2>&1";
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);
            return startInfo;
        }

        var shellCommand = $"exec {QuoteUnixShellArgument(definition.ExecutablePath)} {arguments} >> {QuoteUnixShellArgument(logPath)} 2>&1";
        var unixStartInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        unixStartInfo.ArgumentList.Add("-c");
        unixStartInfo.ArgumentList.Add(shellCommand);
        return unixStartInfo;
    }

    private bool TryGetRunningProcess(
        ApplicationResourceDefinition? definition,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Process? process)
    {
        process = null;
        if (definition is null)
        {
            return false;
        }

        if (_processes.TryGetValue(definition.Id, out var state))
        {
            if (!state.Process.HasExited)
            {
                process = state.Process;
                return true;
            }

            runtimeStates.Save(new ApplicationRuntimeState(
                definition.Id,
                state.Process.Id,
                null,
                DateTimeOffset.UtcNow,
                TryGetExitCode(state.Process),
                state.LogPath));
        }

        var runtimeState = runtimeStates.Get(definition.Id);
        if (runtimeState?.LastKnownProcessId is null ||
            runtimeState.LastKnownProcessStartedAt is null)
        {
            return false;
        }

        try
        {
            var candidate = Process.GetProcessById(runtimeState.LastKnownProcessId.Value);
            if (candidate.HasExited ||
                !ProcessStartMatches(candidate, runtimeState.LastKnownProcessStartedAt.Value))
            {
                return false;
            }

            var logPath = runtimeState.LogPath ?? GetLogPath(definition.Id);
            var log = new ApplicationProcessLog(logPath);
            candidate.EnableRaisingEvents = true;
            candidate.Exited += (_, _) =>
            {
                log.Append(
                    $"Process exited with code {TryGetExitCode(candidate)?.ToString() ?? "unknown"}.",
                    "process",
                    TryGetExitCode(candidate) == 0 ? "Information" : "Error");
                runtimeStates.Save(new ApplicationRuntimeState(
                    definition.Id,
                    candidate.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    TryGetExitCode(candidate),
                    logPath));
            };

            _processes[definition.Id] = new ApplicationProcessState(
                candidate,
                log,
                definition.Lifetime,
                logPath);
            process = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private ApplicationProcessLog GetProcessLog(string applicationId)
    {
        if (_processes.TryGetValue(applicationId, out var state))
        {
            return state.Log;
        }

        return new ApplicationProcessLog(
            runtimeStates.Get(applicationId)?.LogPath ?? GetLogPath(applicationId));
    }

    private string GetLogPath(string applicationId)
    {
        var logDirectory = Path.IsPathRooted(options.LogDirectory)
            ? options.LogDirectory
            : Path.GetFullPath(options.LogDirectory, environment.ContentRootPath);
        var logFileName = SlugPattern()
            .Replace(applicationId.ToLowerInvariant(), "-")
            .Trim('-');

        return Path.Combine(logDirectory, $"{logFileName}.log");
    }

    private static bool ProcessStartMatches(
        Process process,
        DateTimeOffset expectedStartedAt)
    {
        var actualStartedAt = TryGetStartTime(process);
        if (actualStartedAt is null)
        {
            return true;
        }

        return (actualStartedAt.Value - expectedStartedAt).Duration() <= TimeSpan.FromSeconds(2);
    }

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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
            Lifetime = definition.Lifetime,
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

    private string CreateUniqueImportId(string name)
    {
        var candidate = CreateId(name);
        if (store.GetApplication(candidate) is null)
        {
            return candidate;
        }

        var suffix = 2;
        while (store.GetApplication($"{candidate}-{suffix}") is not null)
        {
            suffix++;
        }

        return $"{candidate}-{suffix}";
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

    private static string QuoteUnixShellArgument(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string EscapeWindowsCommandArgument(string value) =>
        value.Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string GetLogId(string applicationId) => $"{applicationId}:logs";

    private static string? GetApplicationIdFromLogId(string logId) =>
        logId.EndsWith(":logs", StringComparison.OrdinalIgnoreCase)
            ? logId[..^":logs".Length]
            : null;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugPattern();

    private sealed record ApplicationProcessState(
        Process Process,
        ApplicationProcessLog Log,
        ApplicationLifetime Lifetime,
        string LogPath);

    private sealed record ApplicationResourceTemplateConfiguration(
        string ExecutablePath,
        string? Arguments,
        string? WorkingDirectory,
        string? Endpoint,
        IReadOnlyList<EnvironmentVariableAssignment> EnvironmentVariables,
        ApplicationLifetime Lifetime);
}
