namespace CloudShell.Cli;

using System.Diagnostics;
using Spectre.Console;

internal static class CloudShellCli
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        IAnsiConsole console,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var command = CommandLineParser.Parse(args);
            return await ExecuteAsync(command, console, cancellationToken);
        }
        catch (CliUsageException ex)
        {
            console.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            console.WriteLine();
            RenderHelp(console);
            return 2;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(
        CliCommand command,
        IAnsiConsole console,
        CancellationToken cancellationToken)
    {
        var daemon = new ControlPlaneDaemon();
        switch (command)
        {
            case HelpCommand:
                RenderHelp(console);
                return 0;
            case HostNameAddCommand addHostName:
            {
                var mappings = new HostNameMappings();
                var plan = mappings.PlanAdd(
                    addHostName.HostName,
                    addHostName.Address,
                    addHostName.HostsFile);
                await ApplyHostNamePlanAsync(console, mappings, plan, addHostName.DryRun, cancellationToken);
                return 0;
            }
            case HostNameRemoveCommand removeHostName:
            {
                var mappings = new HostNameMappings();
                var plan = mappings.PlanRemove(
                    removeHostName.HostName,
                    removeHostName.HostsFile);
                await ApplyHostNamePlanAsync(console, mappings, plan, removeHostName.DryRun, cancellationToken);
                return 0;
            }
            case ControlPlaneStartCommand start:
            {
                var startedState = await console
                    .Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Starting Control Plane", async _ =>
                        await daemon.StartAsync(start, cancellationToken));
                RenderControlPlaneState(console, "Control Plane", startedState);
                return 0;
            }
            case ControlPlaneStopCommand stop:
            {
                var stopResult = await daemon.StopAsync(stop);
                if (!stopResult.StateFound)
                {
                    console.MarkupLine("[yellow]No local Control Plane daemon state was found.[/]");
                }
                else if (stopResult.WasRunning)
                {
                    console.MarkupLine($"[green]Stopped Control Plane process {stopResult.ProcessId}.[/]");
                }
                else
                {
                    console.MarkupLine($"[yellow]Control Plane process {stopResult.ProcessId} is not running.[/]");
                }

                return 0;
            }
            case ControlPlaneStatusCommand status:
            {
                var statusResult = await daemon.StatusAsync(status, cancellationToken);
                if (statusResult.State is null)
                {
                    console.MarkupLine("[yellow]No local Control Plane daemon state was found.[/]");
                }
                else
                {
                    RenderControlPlaneStatus(console, statusResult);
                }

                return 0;
            }
            case UiOpenCommand openUi:
            {
                var url = openUi.Url ??
                    (await daemon.ReadStateAsync(openUi.StateDirectory))?.BaseUrl;
                if (url is null)
                {
                    throw new CliUsageException(
                        "No CloudShell UI URL was supplied and no local Control Plane daemon state was found. Use --url or control-plane start.");
                }

                OpenBrowser(url);
                console.MarkupLine($"[green]Opened {Markup.Escape(url.ToString())}.[/]");
                return 0;
            }
            case ResourceListCommand list:
            {
                var listControlPlaneUrl = await ResolveControlPlaneUrlAsync(
                    daemon,
                    list.StateDirectory,
                    list.ControlPlaneUrl);
                var resources = await console
                    .Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Listing resources", async _ =>
                        await ResourceOperationsClient.ListAsync(
                            listControlPlaneUrl,
                            list.BearerToken,
                            list.ResourceType,
                            list.ResourceClass,
                            list.IsRegistered,
                            cancellationToken));
                RenderResources(console, resources);
                return 0;
            }
            case ResourceShowCommand show:
            {
                var showControlPlaneUrl = await ResolveControlPlaneUrlAsync(
                    daemon,
                    show.StateDirectory,
                    show.ControlPlaneUrl);
                var resource = await console
                    .Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Loading resource", async _ =>
                        await ResourceOperationsClient.GetAsync(
                            showControlPlaneUrl,
                            show.BearerToken,
                            show.ResourceId,
                            cancellationToken));

                if (resource is null)
                {
                    console.MarkupLine($"[red]Resource '{Markup.Escape(show.ResourceId)}' was not found.[/]");
                    return 1;
                }

                RenderResource(console, resource);
                return 0;
            }
            case ResourceActionExecuteCommand execute:
            {
                var actionControlPlaneUrl = await ResolveControlPlaneUrlAsync(
                    daemon,
                    execute.StateDirectory,
                    execute.ControlPlaneUrl);
                var procedure = await console
                    .Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Executing resource action", async _ =>
                        await ResourceOperationsClient.ExecuteActionAsync(
                            actionControlPlaneUrl,
                            execute.BearerToken,
                            execute,
                            cancellationToken));
                RenderProcedure(console, procedure);
                return 0;
            }
            case TemplateApplyCommand apply:
                if (apply.StartControlPlane)
                {
                    await console
                        .Status()
                        .Spinner(Spinner.Known.Dots)
                        .StartAsync("Starting Control Plane", async _ =>
                            await daemon.StartAsync(
                                new ControlPlaneStartCommand(
                                    apply.StateDirectory,
                                    apply.HostProject,
                                    apply.DataDirectory,
                                    apply.StartUrl,
                                    apply.BearerToken,
                                    apply.NoBuild,
                                    apply.TimeoutSeconds),
                                cancellationToken));
                }

                var templateControlPlaneUrl = await ResolveControlPlaneUrlAsync(
                    daemon,
                    apply.StateDirectory,
                    apply.ControlPlaneUrl);

                var applyResult = await console
                    .Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Applying resource template", async _ =>
                        await ResourceTemplateApplyClient.ApplyAsync(
                            templateControlPlaneUrl,
                            apply.TemplatePath,
                            apply.Mode,
                            apply.BearerToken,
                            cancellationToken));

                console.MarkupLine(applyResult.IsCommitted
                    ? "[green]Template applied.[/]"
                    : "[yellow]Template was not committed.[/]");

                if (applyResult.Diagnostics.Count != 0)
                {
                    var table = new Table()
                        .Title("Diagnostics")
                        .AddColumn("Severity")
                        .AddColumn("Code")
                        .AddColumn("Target")
                        .AddColumn("Message");

                    foreach (var diagnostic in applyResult.Diagnostics)
                    {
                        table.AddRow(
                            Markup.Escape(diagnostic.Severity.ToString()),
                            Markup.Escape(diagnostic.Code),
                            Markup.Escape(diagnostic.Target ?? string.Empty),
                            Markup.Escape(diagnostic.Message));
                    }

                    console.Write(table);
                }

                return applyResult.HasErrors ? 1 : 0;
            default:
                console.MarkupLine("[red]Unsupported command.[/]");
                return 2;
        }
    }

    private static void RenderHelp(IAnsiConsole console) =>
        console.Write(new Panel(new Text(HelpText.Text))
            .Header("CloudShell CLI")
            .Border(BoxBorder.Rounded));

    private static async Task<Uri> ResolveControlPlaneUrlAsync(
        ControlPlaneDaemon daemon,
        string stateDirectory,
        Uri? explicitUrl)
    {
        if (explicitUrl is not null)
        {
            return explicitUrl;
        }

        var state = await daemon.ReadStateAsync(stateDirectory);
        return state?.BaseUrl ??
            throw new CliUsageException(
                "No Control Plane URL was supplied and no local daemon state was found. Use --control-plane or --start.");
    }

    private static void RenderControlPlaneState(
        IAnsiConsole console,
        string title,
        ControlPlaneDaemonState state)
    {
        var table = new Table()
            .Title(title)
            .AddColumn("Field")
            .AddColumn("Value");
        table.AddRow("PID", state.ProcessId.ToString());
        table.AddRow("URL", Markup.Escape(state.BaseUrl.ToString()));
        table.AddRow("Host project", Markup.Escape(state.HostProjectPath));
        if (!string.IsNullOrWhiteSpace(state.DataDirectory))
        {
            table.AddRow("Data directory", Markup.Escape(state.DataDirectory));
        }
        table.AddRow("Started", Markup.Escape(state.StartedAt.ToString("O")));
        console.Write(table);
    }

    private static void RenderResources(
        IAnsiConsole console,
        IReadOnlyList<CloudShell.Abstractions.ResourceManager.Resource> resources)
    {
        var table = new Table()
            .Title("Resources")
            .AddColumn("Id")
            .AddColumn("Name")
            .AddColumn("Type")
            .AddColumn("Class")
            .AddColumn("State")
            .AddColumn("Provider")
            .AddColumn("Actions");

        foreach (var resource in resources)
        {
            table.AddRow(
                Markup.Escape(resource.Id),
                Markup.Escape(resource.DisplayName ?? resource.Name),
                Markup.Escape(resource.EffectiveTypeId),
                Markup.Escape(resource.ResourceClass.ToString()),
                Markup.Escape(resource.State?.ToString() ?? string.Empty),
                Markup.Escape(resource.Provider),
                Markup.Escape(string.Join(", ", resource.ResourceActions.Select(action => action.Id))));
        }

        console.Write(table);
        console.MarkupLine($"[grey]{resources.Count} resource(s).[/]");
    }

    private static void RenderResource(
        IAnsiConsole console,
        CloudShell.Abstractions.ResourceManager.Resource resource)
    {
        var table = new Table()
            .Title("Resource")
            .AddColumn("Field")
            .AddColumn("Value");
        table.AddRow("Id", Markup.Escape(resource.Id));
        table.AddRow("Name", Markup.Escape(resource.Name));
        AddOptionalRow(table, "Display name", resource.DisplayName);
        table.AddRow("Type", Markup.Escape(resource.EffectiveTypeId));
        table.AddRow("Class", Markup.Escape(resource.ResourceClass.ToString()));
        table.AddRow("State", Markup.Escape(resource.State?.ToString() ?? "none"));
        table.AddRow("Provider", Markup.Escape(resource.Provider));
        table.AddRow("Source", Markup.Escape(resource.Source.ToString()));
        table.AddRow("Management", Markup.Escape(resource.ManagementMode.ToString()));
        table.AddRow("Visibility", Markup.Escape(resource.Visibility.ToString()));
        AddOptionalRow(table, "Parent", resource.ParentResourceId);
        AddOptionalRow(table, "Owner", resource.OwnerResourceId);
        AddOptionalRow(table, "Primary endpoint", resource.PrimaryEndpoint == "none" ? null : resource.PrimaryEndpoint);
        table.AddRow("Updated", Markup.Escape(resource.LastUpdated.ToString("O")));
        console.Write(table);

        RenderDependencies(console, resource);
        RenderEndpoints(console, resource);
        RenderActions(console, resource);
        RenderAttributes(console, resource);
        RenderCapabilities(console, resource);
    }

    private static void RenderDependencies(
        IAnsiConsole console,
        CloudShell.Abstractions.ResourceManager.Resource resource)
    {
        if (resource.DependsOn.Count == 0)
        {
            return;
        }

        var table = new Table()
            .Title("Dependencies")
            .AddColumn("Resource Id");
        foreach (var dependency in resource.DependsOn)
        {
            table.AddRow(Markup.Escape(dependency));
        }

        console.Write(table);
    }

    private static void RenderEndpoints(
        IAnsiConsole console,
        CloudShell.Abstractions.ResourceManager.Resource resource)
    {
        if (resource.Endpoints.Count > 0)
        {
            var endpoints = new Table()
                .Title("Endpoints")
                .AddColumn("Name")
                .AddColumn("Protocol")
                .AddColumn("Exposure")
                .AddColumn("Target port");
            foreach (var endpoint in resource.Endpoints)
            {
                endpoints.AddRow(
                    Markup.Escape(endpoint.Name),
                    Markup.Escape(endpoint.Protocol),
                    Markup.Escape(endpoint.Exposure.ToString()),
                    Markup.Escape(endpoint.TargetPort?.ToString() ?? string.Empty));
            }

            console.Write(endpoints);
        }

        if (resource.ResourceEndpointNetworkMappings.Count == 0)
        {
            return;
        }

        var mappings = new Table()
            .Title("Endpoint Addresses")
            .AddColumn("Name")
            .AddColumn("Endpoint")
            .AddColumn("Address")
            .AddColumn("Exposure")
            .AddColumn("Network");
        foreach (var mapping in resource.ResourceEndpointNetworkMappings)
        {
            mappings.AddRow(
                Markup.Escape(mapping.Name),
                Markup.Escape(mapping.Target.EndpointName),
                Markup.Escape(mapping.Address),
                Markup.Escape(mapping.Exposure.ToString()),
                Markup.Escape(mapping.NetworkResourceId ?? string.Empty));
        }

        console.Write(mappings);
    }

    private static void RenderActions(
        IAnsiConsole console,
        CloudShell.Abstractions.ResourceManager.Resource resource)
    {
        if (resource.ResourceActions.Count == 0)
        {
            return;
        }

        var table = new Table()
            .Title("Actions")
            .AddColumn("Id")
            .AddColumn("Name")
            .AddColumn("Kind")
            .AddColumn("Confirmation")
            .AddColumn("Permission");
        foreach (var action in resource.ResourceActions)
        {
            table.AddRow(
                Markup.Escape(action.Id),
                Markup.Escape(action.DisplayName),
                Markup.Escape(action.Kind.ToString()),
                action.RequiresConfirmation ? "yes" : "no",
                Markup.Escape(action.RequiredPermission ?? string.Empty));
        }

        console.Write(table);
    }

    private static void RenderAttributes(
        IAnsiConsole console,
        CloudShell.Abstractions.ResourceManager.Resource resource)
    {
        if (resource.ResourceAttributes.Count == 0)
        {
            return;
        }

        var table = new Table()
            .Title("Attributes")
            .AddColumn("Name")
            .AddColumn("Value");
        foreach (var attribute in resource.ResourceAttributes.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase))
        {
            table.AddRow(
                Markup.Escape(attribute.Key),
                Markup.Escape(attribute.Value));
        }

        console.Write(table);
    }

    private static void RenderCapabilities(
        IAnsiConsole console,
        CloudShell.Abstractions.ResourceManager.Resource resource)
    {
        if (resource.ResourceCapabilities.Count == 0)
        {
            return;
        }

        var table = new Table()
            .Title("Capabilities")
            .AddColumn("Id");
        foreach (var capability in resource.ResourceCapabilities.OrderBy(capability => capability.Id, StringComparer.OrdinalIgnoreCase))
        {
            table.AddRow(Markup.Escape(capability.Id));
        }

        console.Write(table);
    }

    private static void AddOptionalRow(
        Table table,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            table.AddRow(name, Markup.Escape(value));
        }
    }

    private static void RenderProcedure(
        IAnsiConsole console,
        CloudShell.Abstractions.ResourceManager.ResourceProcedureResult procedure)
    {
        console.MarkupLine($"[green]{Markup.Escape(procedure.Message)}[/]");
        if (procedure.RestartRequired && !string.IsNullOrWhiteSpace(procedure.RestartResourceId))
        {
            console.MarkupLine($"[yellow]Restart required: {Markup.Escape(procedure.RestartResourceId)}[/]");
        }

        if (procedure.RuntimeReconciliationRequired &&
            !string.IsNullOrWhiteSpace(procedure.RuntimeReconciliationResourceId))
        {
            console.MarkupLine(
                $"[yellow]Runtime reconciliation required: {Markup.Escape(procedure.RuntimeReconciliationResourceId)}[/]");
        }

        if (procedure.Signals.Count == 0)
        {
            return;
        }

        var table = new Table()
            .Title("Signals")
            .AddColumn("Severity")
            .AddColumn("Message");
        foreach (var signal in procedure.Signals)
        {
            table.AddRow(
                Markup.Escape(signal.Severity.ToString()),
                Markup.Escape(signal.Message));
        }

        console.Write(table);
    }

    private static async Task ApplyHostNamePlanAsync(
        IAnsiConsole console,
        HostNameMappings mappings,
        HostNameMappingPlan plan,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        RenderHostNamePlan(console, plan);
        if (dryRun)
        {
            console.MarkupLine("[yellow]Dry run only. No hosts file changes were written.[/]");
            return;
        }

        try
        {
            await mappings.ApplyAsync(plan, cancellationToken);
            console.MarkupLine("[green]Hosts file updated.[/]");
        }
        catch (UnauthorizedAccessException)
        {
            throw new CliUsageException(
                $"Permission denied writing '{plan.HostsFile}'. Re-run the command with elevated privileges or use --hosts-file.");
        }
    }

    private static void RenderHostNamePlan(IAnsiConsole console, HostNameMappingPlan plan)
    {
        var table = new Table()
            .Title(plan.Add ? "Add Local Host Name" : "Remove Local Host Name")
            .AddColumn("Field")
            .AddColumn("Value");
        table.AddRow("Hosts file", Markup.Escape(plan.HostsFile));
        table.AddRow("Host name", Markup.Escape(plan.HostName));
        if (plan.Address is not null)
        {
            table.AddRow("Address", Markup.Escape(plan.Address));
        }

        console.Write(table);
    }

    private static void RenderControlPlaneStatus(
        IAnsiConsole console,
        ControlPlaneDaemonStatus status)
    {
        var state = status.State!;
        var table = new Table()
            .Title("Control Plane Status")
            .AddColumn("Field")
            .AddColumn("Value");
        table.AddRow("PID", state.ProcessId.ToString());
        table.AddRow("URL", Markup.Escape(state.BaseUrl.ToString()));
        table.AddRow("Host project", Markup.Escape(state.HostProjectPath));
        if (!string.IsNullOrWhiteSpace(state.DataDirectory))
        {
            table.AddRow("Data directory", Markup.Escape(state.DataDirectory));
        }
        table.AddRow("Started", Markup.Escape(state.StartedAt.ToString("O")));
        table.AddRow("Process", status.ProcessRunning ? "[green]running[/]" : "[red]stopped[/]");
        table.AddRow("Control Plane API", status.ApiReady ? "[green]ready[/]" : "[yellow]not ready[/]");
        console.Write(table);
    }

    private static void OpenBrowser(Uri url)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"")
            {
                CreateNoWindow = true
            }
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open", url.ToString())
                : new ProcessStartInfo("xdg-open", url.ToString());

        startInfo.UseShellExecute = false;
        Process.Start(startInfo);
    }
}
