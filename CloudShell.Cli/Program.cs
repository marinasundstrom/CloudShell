using CloudShell.Cli;
using Spectre.Console;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await CloudShellCli.RunAsync(args, AnsiConsole.Console, cancellation.Token);
