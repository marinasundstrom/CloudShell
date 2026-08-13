namespace CloudShell.Cli;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Spectre.Console;

internal sealed class ForegroundDevelopmentHostRunner
{
    internal const string DevelopmentHostAssemblyName = "CloudShell.LocalDevelopmentHost.dll";

    public async Task<int> RunAsync(
        RunCommand command,
        IAnsiConsole console,
        CancellationToken cancellationToken)
    {
        var templatePath = Path.GetFullPath(command.TemplatePath);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException(
                $"The resource template '{templatePath}' does not exist.",
                templatePath);
        }

        var workingDirectory = Path.GetDirectoryName(templatePath) ?? Environment.CurrentDirectory;
        var dataDirectory = ResolvePath(command.DataDirectory, workingDirectory);
        var hostSettingsPath = ResolveHostSettingsPath(command.HostSettingsPath, workingDirectory);
        var bearerToken = await CliCredentialResolver.ResolveBearerTokenAsync(
            command.BearerToken,
            cancellationToken);
        var startInfo = CreateStartInfo(
            command.HostProject,
            workingDirectory,
            dataDirectory,
            hostSettingsPath,
            command.Url);

        console.MarkupLine($"[grey]Starting CloudShell development host {Markup.Escape(command.Url.ToString())}[/]");
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start the CloudShell development host.");

        try
        {
            await WaitForReadyAsync(
                command.Url,
                bearerToken,
                process,
                TimeSpan.FromSeconds(command.TimeoutSeconds),
                cancellationToken);

            var applyResult = await ResourceTemplateApplyClient.ApplyAsync(
                command.Url,
                templatePath,
                command.Mode,
                bearerToken,
                cancellationToken);

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

            if (applyResult.HasErrors || !applyResult.IsCommitted)
            {
                console.MarkupLine("[red]Resource template was not applied.[/]");
                return 1;
            }

            console.MarkupLine($"[green]CloudShell host: {Markup.Escape(command.Url.ToString())}[/]");
            console.MarkupLine("[grey]Press Ctrl+C to stop.[/]");
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 130;
        }
        finally
        {
            await StopAsync(process);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string? hostProject,
        string workingDirectory,
        string dataDirectory,
        string? hostSettingsPath,
        Uri url,
        string? applicationBaseDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        if (!string.IsNullOrWhiteSpace(hostProject))
        {
            var fullHostProject = Path.GetFullPath(hostProject, workingDirectory);
            if (!File.Exists(fullHostProject))
            {
                throw new FileNotFoundException(
                    $"The host project '{fullHostProject}' does not exist.",
                    fullHostProject);
            }

            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(fullHostProject);
            startInfo.ArgumentList.Add("--no-launch-profile");
            startInfo.ArgumentList.Add("--");
        }
        else
        {
            startInfo.ArgumentList.Add(ResolveDevelopmentHostAssembly(
                applicationBaseDirectory ?? AppContext.BaseDirectory,
                workingDirectory));
        }

        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(url.ToString());
        startInfo.ArgumentList.Add("--CloudShell:DataDirectory");
        startInfo.ArgumentList.Add(dataDirectory);
        if (hostSettingsPath is not null)
        {
            startInfo.ArgumentList.Add("--CloudShell:HostSettingsPath");
            startInfo.ArgumentList.Add(hostSettingsPath);
        }
        else if (url.IsLoopback)
        {
            startInfo.ArgumentList.Add("--Authentication:Enabled");
            startInfo.ArgumentList.Add("false");
        }

        return startInfo;
    }

    internal static string ResolveDevelopmentHostAssembly(
        string applicationBaseDirectory,
        string searchDirectory)
    {
        var bundledHost = Path.Combine(
            applicationBaseDirectory,
            "host",
            DevelopmentHostAssemblyName);
        if (File.Exists(bundledHost))
        {
            return bundledHost;
        }

        var directory = new DirectoryInfo(searchDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "CloudShell.LocalDevelopmentHost",
                "bin",
                "Debug",
                "net11.0",
                DevelopmentHostAssemblyName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "The bundled CloudShell development host was not found. Reinstall the CloudShell.Cli tool or use --host-project.",
            bundledHost);
    }

    private static string ResolvePath(string path, string basePath) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, basePath);

    private static string? ResolveHostSettingsPath(string? configuredPath, string basePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = ResolvePath(configuredPath, basePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"The host settings file '{fullPath}' does not exist.",
                    fullPath);
            }

            return fullPath;
        }

        var conventionalPath = Path.Combine(basePath, "appsettings.json");
        return File.Exists(conventionalPath) ? conventionalPath : null;
    }

    private static async Task WaitForReadyAsync(
        Uri baseUrl,
        string? bearerToken,
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            while (!linked.Token.IsCancellationRequested)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"CloudShell development host exited with code {process.ExitCode} before it was ready.");
                }

                if (await IsReadyAsync(baseUrl, bearerToken, linked.Token))
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), linked.Token);
            }

            linked.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"CloudShell development host did not become ready within {timeout.TotalSeconds:N0} seconds.");
        }
    }

    private static async Task<bool> IsReadyAsync(
        Uri baseUrl,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = NormalizeBaseAddress(baseUrl) };
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            using var response = await client.GetAsync(
                "api/control-plane/v1/resources",
                cancellationToken);
            return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static Uri NormalizeBaseAddress(Uri baseUrl)
    {
        var value = baseUrl.ToString();
        return value.EndsWith('/', StringComparison.Ordinal)
            ? baseUrl
            : new Uri(value + "/", UriKind.Absolute);
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and the stop request.
        }

        if (!process.HasExited)
        {
            await process.WaitForExitAsync();
        }
    }
}
