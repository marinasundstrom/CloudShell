using System.Globalization;
using System.Text;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Traefik;

public sealed class TraefikLoadBalancerProvider(TraefikProviderOptions options) : ILoadBalancerProvider
{
    public string ProviderName => "traefik";

    public bool CanApply(LoadBalancerProviderContext context) =>
        string.Equals(context.Definition.Provider, ProviderName, StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceProcedureResult> ApplyAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var configuration = TraefikDynamicConfigurationWriter.Write(context);
        Directory.CreateDirectory(options.DynamicConfigurationDirectory);
        var path = Path.Combine(
            options.DynamicConfigurationDirectory,
            $"{CreateFileName(context.Definition.Id)}.dynamic.yml");
        await File.WriteAllTextAsync(path, configuration, Encoding.UTF8, cancellationToken);

        return ResourceProcedureResult.Completed(
            $"Applied Traefik configuration for {context.Definition.LoadBalancerRoutes.Count.ToString(CultureInfo.InvariantCulture)} route(s) to {path}.");
    }

    private static string CreateFileName(string resourceId)
    {
        var builder = new StringBuilder(resourceId.Length);
        foreach (var character in resourceId.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        var fileName = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(fileName) ? "load-balancer" : fileName;
    }
}
