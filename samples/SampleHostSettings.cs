using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

internal static class SampleHostSettings
{
    public static WebApplicationOptions CreateWebApplicationOptions(string[] args)
    {
        var hostSettings = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("hostsettings.json", optional: true)
            .AddEnvironmentVariables(prefix: "DOTNET_")
            .AddEnvironmentVariables(prefix: "ASPNETCORE_")
            .AddCommandLine(args)
            .Build();

        return new WebApplicationOptions
        {
            Args = args,
            EnvironmentName = hostSettings[HostDefaults.EnvironmentKey]
        };
    }
}
