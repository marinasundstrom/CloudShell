using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Api;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.Logs;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudShell.ControlPlane.Hosting;

public static class CloudShellControlPlaneApplicationBuilderExtensions
{
    public static IControlPlaneBuilder AddCloudShellControlPlane(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpClient();
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        builder.Services.AddCloudShellControlPlaneOpenApi();

        ConfigurePersistence(builder);
        builder.Services.AddCloudShellAuthentication(builder.Configuration);

        var controlPlane = builder.Services.AddControlPlane();

        builder.Services.AddScoped<IResourceGroupStore, AuthorizedResourceGroupStore>();
        builder.Services.AddScoped<IResourceRegistrationStore, AuthorizedResourceRegistrationStore>();
        builder.Services.AddScoped<IResourceManagerStore, ResourceManagerStore>();
        builder.Services.AddScoped<ILogStore, LogStore>();
        builder.Services.AddScoped<ResourceTemplateService>();

        return controlPlane;
    }

    private static void ConfigurePersistence(WebApplicationBuilder builder)
    {
        var persistenceOptions = builder.Configuration
            .GetSection(CloudShellPersistenceOptions.SectionName)
            .Get<CloudShellPersistenceOptions>()
            ?? new CloudShellPersistenceOptions();

        ResolveSqlitePaths(persistenceOptions, builder.Environment.ContentRootPath);
        builder.Services.AddCloudShellPersistence(persistenceOptions);
    }

    private static void ResolveSqlitePaths(
        CloudShellPersistenceOptions options,
        string contentRootPath)
    {
        if (!options.Provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        options.ConnectionString = ResolveSqlitePath(
            options.ConnectionString,
            contentRootPath);
        options.IdentityConnectionString = ResolveSqlitePath(
            options.IdentityConnectionString,
            contentRootPath);
    }

    private static string ResolveSqlitePath(string connectionString, string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(builder.DataSource))
        {
            return connectionString;
        }

        var fullPath = Path.GetFullPath(builder.DataSource, contentRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        builder.DataSource = fullPath;
        return builder.ConnectionString;
    }
}
