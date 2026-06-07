using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CloudShell.Hosting;

public static class CloudShellHostApplicationExtensions
{
    public static Task<WebApplication> UseCloudShellAsync(this WebApplication app) =>
        app.UseCloudShellUiAsync();

    public static Task<WebApplication> UseCloudShellUiAsync(this WebApplication app) =>
        UseCloudShellUiCoreAsync(app);

    private static Task<WebApplication> UseCloudShellUiCoreAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();
        app.UseRequestLocalization();

        var extensionRegistry = app.Services.GetRequiredService<CloudShellExtensionRegistry>();
        extensionRegistry.Validate(app.Services.GetRequiredService<ICloudShellExtensionActivationStore>());

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseAntiforgery();

        return Task.FromResult(app);
    }

    public static RazorComponentsEndpointConventionBuilder MapCloudShell<TRootComponent>(
        this WebApplication app)
        where TRootComponent : IComponent =>
        app.MapCloudShellUi<TRootComponent>();

    public static RazorComponentsEndpointConventionBuilder MapCloudShellUi<TRootComponent>(
        this WebApplication app)
        where TRootComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapCloudShellLocalization();
        app.MapStaticAssets().AllowAnonymous();

        return app.MapCloudShellRazorComponents<TRootComponent>();
    }

    private static RazorComponentsEndpointConventionBuilder MapCloudShellRazorComponents<TRootComponent>(
        this WebApplication app)
        where TRootComponent : IComponent
    {
        var razorComponents = app.MapRazorComponents<TRootComponent>()
            .AddInteractiveServerRenderMode();

        var extensionRegistry = app.Services.GetRequiredService<CloudShellExtensionRegistry>();
        var extensionAssemblies = extensionRegistry.ViewAssemblies
            .Where(assembly => assembly != typeof(TRootComponent).Assembly)
            .ToArray();

        if (extensionAssemblies.Length > 0)
        {
            razorComponents.AddAdditionalAssemblies(extensionAssemblies);
        }

        return razorComponents;
    }

    private static IEndpointRouteBuilder MapCloudShellLocalization(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/localization/set", (
            string culture,
            string? returnUrl,
            HttpContext httpContext,
            IOptions<RequestLocalizationOptions> options) =>
        {
            var supported = options.Value.SupportedUICultures ?? [];
            if (supported.Any(supportedCulture =>
                    supportedCulture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase)))
            {
                httpContext.Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddYears(1),
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax,
                        Secure = httpContext.Request.IsHttps
                    });
            }

            return Results.LocalRedirect(IsLocalReturnUrl(returnUrl) ? returnUrl! : "/");
        })
        .AllowAnonymous()
        .ExcludeFromDescription();

        return endpoints;
    }

    private static bool IsLocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) &&
        returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal);
}
