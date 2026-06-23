using CloudShell.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;

namespace CloudShell.Providers.Applications;

public static class SqlServerCredentialApiExtensions
{
    public static RouteGroupBuilder MapCloudShellSqlServerCredentialApi(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints
            .MapGroup("/api/sql-server/v1")
            .WithTags("SQL Server")
            .WithGroupName("cloudshell-sql-server");

        api.MapPost("/credentials", ResolveCredential)
            .WithName("CloudShellSqlServer_ResolveCredential")
            .Accepts<ResolveSqlServerCredentialRequest>("application/json")
            .Produces<ResolveSqlServerCredentialResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        return api;
    }

    private static async Task<IResult> ResolveCredential(
        ResolveSqlServerCredentialRequest request,
        HttpContext httpContext,
        ISqlServerCredentialResolutionOperations credentials,
        CancellationToken cancellationToken)
    {
        try
        {
            var permission = string.IsNullOrWhiteSpace(request.Permission)
                ? DatabaseResourceOperationPermissions.ReadWrite
                : request.Permission.Trim();
            var result = await credentials.ResolveSqlServerCredentialAsync(
                request.SqlServerResourceName,
                request.DatabaseName,
                permission,
                httpContext.User,
                cancellationToken);

            return Results.Ok(new ResolveSqlServerCredentialResponse(
                result.ConnectionString,
                result.ExpiresOn));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(
                title: "SQL Server credential access denied",
                detail: exception.Message,
                statusCode: httpContext.User.Identity?.IsAuthenticated == true
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status401Unauthorized);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.Problem(
                title: "SQL Server credential request invalid",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (SqlException exception)
        {
            return Results.Problem(
                title: "SQL Server credential resolution failed",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public sealed record ResolveSqlServerCredentialRequest(
    string SqlServerResourceName,
    string DatabaseName,
    string? Permission = null);

public sealed record ResolveSqlServerCredentialResponse(
    string ConnectionString,
    DateTimeOffset? ExpiresOn = null);

public sealed record SqlServerCredentialResolutionResult(
    string ConnectionString,
    DateTimeOffset ExpiresOn);
