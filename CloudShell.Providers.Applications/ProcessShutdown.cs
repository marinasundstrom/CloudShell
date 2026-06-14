using System.Diagnostics;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal static class ProcessShutdown
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static void KillProcessTreeAndWait(Process process)
    {
        var descendants = GetDescendantProcesses(process.Id);
        var deadline = DateTimeOffset.UtcNow.Add(DefaultTimeout);
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            WaitForExit(process, deadline);
        }

        foreach (var descendant in descendants)
        {
            try
            {
                WaitForExit(descendant, deadline);
            }
            finally
            {
                descendant.Dispose();
            }
        }
    }

    private static IReadOnlyList<Process> GetDescendantProcesses(int processId)
    {
        if (OperatingSystem.IsWindows())
        {
            return [];
        }

        var descendants = new List<Process>();
        var pending = new Queue<int>();
        pending.Enqueue(processId);

        while (pending.TryDequeue(out var parentId))
        {
            foreach (var childId in GetChildProcessIds(parentId))
            {
                try
                {
                    var child = Process.GetProcessById(childId);
                    descendants.Add(child);
                    pending.Enqueue(childId);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                }
            }
        }

        return descendants;
    }

    private static IReadOnlyList<int> GetChildProcessIds(int parentId)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pgrep",
                Arguments = $"-P {parentId.ToString(CultureInfo.InvariantCulture)}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process is null || !process.WaitForExit((int)TimeSpan.FromSeconds(1).TotalMilliseconds))
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                    ? id
                    : 0)
                .Where(id => id > 0)
                .ToArray();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    private static void WaitForExit(Process process, DateTimeOffset deadline)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            var timeout = deadline - DateTimeOffset.UtcNow;
            if (timeout <= TimeSpan.Zero)
            {
                return;
            }

            process.WaitForExit(timeout);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
