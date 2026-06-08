using CloudShell.Abstractions.Authentication;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.Authentication;
using CloudShell.Hosting.Localization;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FluentUI.AspNetCore.Components;
using System.Globalization;

namespace CloudShell.Hosting;

public static class CloudShellHostApplicationBuilderExtensions
{
    public static IControlPlaneBuilder AddCloudShell(
        this WebApplicationBuilder builder) =>
        builder.AddCloudShellUi();

    public static IControlPlaneBuilder AddCloudShellUi(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpClient();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.TryAddScoped<ICloudShellAuthorizationService, PermissiveAuthorizationService>();
        builder.Services.TryAddScoped<IAccountService, ExternalAccountService>();
        builder.Services.TryAddSingleton<IResourceOrchestrationSettings, LocalResourceOrchestrationSettings>();
        builder.Services.TryAddScoped<IResourceOrchestrationCatalog, LocalResourceOrchestrationCatalog>();

        var cloudShell = builder.Services
            .AddCloudShell()
            .AddExtension<CoreShellExtension>();

        AddCloudShellUiServices(builder);

        return cloudShell;
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
