using System.Diagnostics;

namespace CloudShell.Providers.Applications;

internal static class ProcessShutdown
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static void KillProcessTreeAndWait(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(DefaultTimeout);
        }
    }
}
