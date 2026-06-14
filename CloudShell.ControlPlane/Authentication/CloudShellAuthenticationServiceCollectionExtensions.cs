using CloudShell.Abstractions.Authentication;
using CloudShell.Abstractions.Authorization;
using CloudShell.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CloudShell.ControlPlane.Authentication;

public static class CloudShellAuthenticationServiceCollectionExtensions
{
    public static CloudShellAuthenticationOptions AddCloudShellAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(CloudShellAuthenticationOptions.SectionName)
            .Get<CloudShellAuthenticationOptions>()
            ?? new CloudShellAuthenticationOptions();

        services.Configure<CloudShellAuthenticationOptions>(
            configuration.GetSection(CloudShellAuthenticationOptions.SectionName));
        services.AddHttpContextAccessor();
        services.AddScoped<ICloudShellAuthorizationService, ClaimsCloudShellAuthorizationService>();
        services.AddScoped<CloudShellSecretSignInService>();
        services.AddScoped<CloudShellAccountService>();
        services.AddScoped<IAccountService>(
            serviceProvider => serviceProvider.GetRequiredService<CloudShellAccountService>());
        services.AddCascadingAuthenticationState();
        services.AddSingleton<CloudShellBearerTokenValidationService>();
        if (options.BuiltInAuthority.Enabled)
        {
            services.AddSingleton<BuiltInAuthorityTokenService>();
        }

        if (!options.Enabled)
        {
            services.AddAuthentication();
            services.AddAuthorization();
            return options;
        }

        switch (options.Mode.ToUpperInvariant())
        {
            case "IDENTITY":
                services
                    .AddIdentity<IdentityUser, IdentityRole>(identity =>
                    {
                        identity.Password.RequiredLength = 12;
                        identity.Password.RequireDigit = true;
                        identity.Password.RequireLowercase = true;
                        identity.Password.RequireUppercase = true;
                        identity.Password.RequireNonAlphanumeric = true;
                        identity.User.RequireUniqueEmail = true;
                    })
                    .AddEntityFrameworkStores<CloudShellIdentityDbContext>()
                    .AddDefaultTokenProviders();
                services.ConfigureApplicationCookie(cookie =>
                {
                    cookie.LoginPath = "/account/login";
                    cookie.AccessDeniedPath = "/account/access-denied";
                    cookie.SlidingExpiration = true;
                });
                services.AddScoped<CloudShellIdentitySeeder>();
                break;

            case "SECRET":
                if (string.IsNullOrWhiteSpace(options.Secret))
                {
                    throw new InvalidOperationException(
                        "Authentication:Secret must be configured when Authentication:Mode is 'Secret'.");
                }

                services
                    .AddAuthentication(authentication =>
                    {
                        authentication.DefaultAuthenticateScheme = options.DefaultScheme;
                        authentication.DefaultSignInScheme = options.DefaultScheme;
                        authentication.DefaultChallengeScheme = options.DefaultScheme;
                    })
                    .AddCookie(options.DefaultScheme, cookie =>
                    {
                        cookie.LoginPath = "/account/login";
                        cookie.AccessDeniedPath = "/account/access-denied";
                        cookie.SlidingExpiration = true;
                    });
                break;

            case "OPENIDCONNECT":
                ValidateOpenIdConnect(options);
                services
                    .AddAuthentication(authentication =>
                    {
                        authentication.DefaultAuthenticateScheme = options.DefaultScheme;
                        authentication.DefaultSignInScheme = options.DefaultScheme;
                        authentication.DefaultChallengeScheme = options.ChallengeScheme;
                    })
                    .AddCookie(options.DefaultScheme, cookie =>
                    {
                        cookie.LoginPath = "/account/login";
                        cookie.AccessDeniedPath = "/account/access-denied";
                        cookie.SlidingExpiration = true;
                    })
                    .AddOpenIdConnect(options.ChallengeScheme, oidc =>
                    {
                        oidc.Authority = options.OpenIdConnect.Authority;
                        oidc.MetadataAddress = options.OpenIdConnect.MetadataAddress;
                        oidc.ClientId = options.OpenIdConnect.ClientId;
                        oidc.ClientSecret = options.OpenIdConnect.ClientSecret;
                        oidc.CallbackPath = options.OpenIdConnect.CallbackPath;
                        oidc.RequireHttpsMetadata = options.OpenIdConnect.RequireHttpsMetadata;
                        oidc.GetClaimsFromUserInfoEndpoint =
                            options.OpenIdConnect.GetClaimsFromUserInfoEndpoint;
                        oidc.ResponseType = "code";
                        oidc.UsePkce = true;
                        oidc.SaveTokens = true;
                        oidc.TokenValidationParameters = new TokenValidationParameters
                        {
                            NameClaimType = "name",
                            RoleClaimType = options.RoleClaimType
                        };
                        oidc.Scope.Clear();
                        foreach (var scope in options.OpenIdConnect.Scopes)
                        {
                            oidc.Scope.Add(scope);
                        }
                    });
                break;

            case "EXTERNAL":
                services.AddAuthentication(authentication =>
                {
                    authentication.DefaultAuthenticateScheme = options.DefaultScheme;
                    authentication.DefaultSignInScheme = options.DefaultScheme;
                    authentication.DefaultChallengeScheme = options.ChallengeScheme;
                });
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported authentication mode '{options.Mode}'. " +
                    "Use 'Identity', 'Secret', 'OpenIdConnect', or 'External'.");
        }

        services.AddAuthorization(authorization =>
        {
            authorization.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return options;
    }

    private static void ValidateOpenIdConnect(CloudShellAuthenticationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OpenIdConnect.ClientId))
        {
            throw new InvalidOperationException(
                "Authentication:OpenIdConnect:ClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.OpenIdConnect.Authority) &&
            string.IsNullOrWhiteSpace(options.OpenIdConnect.MetadataAddress))
        {
            throw new InvalidOperationException(
                "Authentication:OpenIdConnect:Authority or MetadataAddress is required.");
        }
    }
}
