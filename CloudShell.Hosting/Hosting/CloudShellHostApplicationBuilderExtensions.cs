using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.Authentication;
using CloudShell.Hosting.Localization;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using System.Globalization;

namespace CloudShell.Hosting;

public static class CloudShellHostApplicationBuilderExtensions
{
    public static IControlPlaneBuilder AddCloudShell(
        this WebApplicationBuilder builder) =>
        builder.AddCloudShellHost();

    public static IControlPlaneBuilder AddCloudShellUi(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpClient();
        builder.Services.AddCloudShellAuthentication(builder.Configuration);

        var cloudShell = builder.Services
            .AddCloudShell()
            .AddExtension<CoreShellExtension>();

        AddCloudShellUiServices(builder);

        return cloudShell;
    }

    public static IControlPlaneBuilder AddCloudShellHost(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var controlPlane = builder
            .AddCloudShellControlPlane()
            .AddExtension<CoreShellExtension>()
            .AddExtension<ResourceManagerExtension>()
            .AddExtension<ObservabilityExtension>();

        AddCloudShellUiServices(builder);

        return controlPlane;
    }

    private static void AddCloudShellUiServices(WebApplicationBuilder builder)
    {
        builder.Configuration["ReloadStaticAssetsAtRuntime"] ??= "false";
        StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        builder.Services.AddFluentUIComponents();
        builder.Services.Configure<CloudShellDisplayOptions>(
            builder.Configuration.GetSection(CloudShellDisplayOptions.SectionName));
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        ConfigureLocalization(builder);

        builder.Services.AddSingleton<ShellCatalog>();
        builder.Services.AddScoped<ICloudShellNavigator, CloudShellNavigator>();
        builder.Services.AddScoped<ResourceHealthCheckService>();
    }

    private static void ConfigureLocalization(WebApplicationBuilder builder)
    {
        var localizationOptions = builder.Configuration
            .GetSection(CloudShellLocalizationOptions.SectionName)
            .Get<CloudShellLocalizationOptions>()
            ?? new CloudShellLocalizationOptions();
        var supportedCultures = GetSupportedCultures(localizationOptions);

        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(
                GetDefaultCulture(localizationOptions, supportedCultures));
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });
        builder.Services.Configure<CloudShellLocalizationOptions>(
            builder.Configuration.GetSection(CloudShellLocalizationOptions.SectionName));
    }

    private static List<CultureInfo> GetSupportedCultures(CloudShellLocalizationOptions options)
    {
        var cultures = options.SupportedCultures
            .Append(options.DefaultCulture)
            .Select(CreateCulture)
            .OfType<CultureInfo>()
            .DistinctBy(culture => culture.Name)
            .ToList();

        return cultures.Count == 0
            ? [CultureInfo.GetCultureInfo("en")]
            : cultures;
    }

    private static CultureInfo GetDefaultCulture(
        CloudShellLocalizationOptions options,
        IReadOnlyList<CultureInfo> supportedCultures)
    {
        var defaultCulture = CreateCulture(options.DefaultCulture);
        if (defaultCulture is not null &&
            supportedCultures.Any(culture =>
                culture.Name.Equals(defaultCulture.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return defaultCulture;
        }

        return supportedCultures[0];
    }

    private static CultureInfo? CreateCulture(string cultureName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
