using CloudShell.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ProjectReference.ServiceDefaults;

public static class Extensions
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks();
        builder.Services.AddHttpClient();
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/healthz");
        app.MapGet("/alive", () => Results.Ok(new
        {
            status = "alive",
            service = app.Environment.ApplicationName,
            timestamp = DateTimeOffset.UtcNow
        }));

        return app;
    }

    public static IHttpClientBuilder AddResourceHttpClient(
        this IServiceCollection services,
        string name,
        string resourceName,
        string endpointName) =>
        services.AddHttpClient(name, (serviceProvider, client) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            client.BaseAddress = configuration.GetRequiredResourceUri(resourceName, endpointName);
        });

    public static Uri GetRequiredResourceUri(
        this IConfiguration configuration,
        string resourceName,
        string endpointName) =>
        configuration.GetResourceUri(resourceName, endpointName)
        ?? throw new InvalidOperationException(
            $"Resource endpoint 'services:{resourceName}:{endpointName}' was not found in configuration.");
}
