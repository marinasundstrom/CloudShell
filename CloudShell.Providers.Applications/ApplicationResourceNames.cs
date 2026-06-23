using System.Globalization;
using System.Text;

namespace CloudShell.Providers.Applications;

internal static class ApplicationResourceNames
{
    public static string CreateRuntimeContainerResourceId(string resourceId, int replica) =>
        $"runtime-container:{CreateStableIdentifier(resourceId)}:replica-{Math.Max(1, replica).ToString(CultureInfo.InvariantCulture)}";

    public static string CreateStableIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var identifier = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(identifier) ? "cloudshell" : identifier;
    }
}
