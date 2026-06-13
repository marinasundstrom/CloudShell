using CloudShell.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CloudShell.Providers.Configuration;

public static class ConfigurationApiExtensions
{
    public static RouteGroupBuilder MapCloudShellConfigurationApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints
            .MapGroup("/api/configuration")
            .WithTags("Configuration");

        api.MapGet("/entries", ListEntriesByQuery)
            .WithName("CloudShellConfiguration_ListEntriesByResourceId")
            .AllowAnonymous();

        api.MapGet("/entries/{name}", GetEntryByQuery)
            .WithName("CloudShellConfiguration_GetEntryByResourceId")
            .AllowAnonymous();

        api.MapGet("/stores/{storeId}/entries", ListEntries)
            .WithName("CloudShellConfiguration_ListEntries")
            .AllowAnonymous();

        api.MapGet("/stores/{storeId}/entries/{name}", GetEntry)
            .WithName("CloudShellConfiguration_GetEntry")
            .AllowAnonymous();

        return api;
    }

    private static IResult ListEntriesByQuery(
        string resourceId,
        HttpRequest request,
        ConfigurationResourceProvider provider) =>
        ListEntries(resourceId, request, provider);

    private static IResult GetEntryByQuery(
        string resourceId,
        string name,
        HttpRequest request,
        ConfigurationResourceProvider provider) =>
        GetEntry(resourceId, name, request, provider);

    private static IResult ListEntries(
        string storeId,
        HttpRequest request,
        ConfigurationResourceProvider provider)
    {
        var configurationStore = provider.GetStore(storeId);
        if (string.IsNullOrWhiteSpace(GetBearerToken(request)))
        {
            return Unauthorized();
        }

        if (configurationStore is null ||
            !IsAuthorized(request, provider, storeId))
        {
            return NotFound();
        }

        return Results.Ok(configurationStore.Entries.Select(ToResponse).ToArray());
    }

    private static IResult GetEntry(
        string storeId,
        string name,
        HttpRequest request,
        ConfigurationResourceProvider provider)
    {
        if (string.IsNullOrWhiteSpace(GetBearerToken(request)))
        {
            return Unauthorized();
        }

        if (provider.GetStore(storeId) is null ||
            !IsAuthorized(request, provider, storeId))
        {
            return NotFound();
        }

        var entry = provider.GetStore(storeId)?.Entries.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

        return entry is null
            ? NotFound()
            : Results.Ok(ToResponse(entry));
    }

    private static ConfigurationEntryResponse ToResponse(ConfigurationEntry entry) =>
        new(entry.Name, entry.Value, entry.IsSecret);

    private static bool IsAuthorized(
        HttpRequest request,
        ConfigurationResourceProvider provider,
        string storeId) =>
        ResourcePermissionClaimAuthorization.HasResourcePermission(
            request.HttpContext.User,
            storeId,
            ConfigurationStoreResourceOperationPermissions.ReadEntries);

    private static string? GetBearerToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.FirstOrDefault();
        const string bearerPrefix = "Bearer ";
        return authorization?.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) == true
            ? authorization[bearerPrefix.Length..].Trim()
            : null;
    }

    private static IResult Unauthorized() =>
        Results.Problem(
            "A configuration bearer token is required.",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized");

    private static IResult NotFound() =>
        Results.Problem(
            "The Configuration Store or entry was not found.",
            statusCode: StatusCodes.Status404NotFound,
            title: "Not found");
}

public sealed record ConfigurationEntryResponse(
    string Name,
    string Value,
    bool IsSecret);
