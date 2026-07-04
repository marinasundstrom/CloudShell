using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CloudShell.ControlPlane.Providers;

public static class RabbitMQCredentialApiExtensions
{
    public static RouteGroupBuilder MapCloudShellRabbitMQCredentialApi(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints
            .MapGroup("/api/rabbitmq/v1")
            .WithTags("RabbitMQ");

        api.MapPost("/credentials", ResolveCredential)
            .WithName("CloudShellRabbitMQ_ResolveCredential")
            .Accepts<ResolveRabbitMQCredentialRequest>("application/json")
            .Produces<ResolveRabbitMQCredentialResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        return api;
    }

    private static async Task<IResult> ResolveCredential(
        ResolveRabbitMQCredentialRequest request,
        HttpContext httpContext,
        RabbitMQCredentialResolver credentials,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await credentials.ResolveAsync(
                request,
                httpContext.User,
                cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(
                title: "RabbitMQ credential access denied",
                detail: exception.Message,
                statusCode: httpContext.User.Identity?.IsAuthenticated == true
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status401Unauthorized);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.Problem(
                title: "RabbitMQ credential request invalid",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }
}

public sealed record ResolveRabbitMQCredentialRequest(
    string RabbitMQResourceName,
    string? Permission = null);

public sealed record ResolveRabbitMQCredentialResponse(
    string Username,
    string Password,
    string VirtualHost,
    DateTimeOffset? ExpiresOn = null);
