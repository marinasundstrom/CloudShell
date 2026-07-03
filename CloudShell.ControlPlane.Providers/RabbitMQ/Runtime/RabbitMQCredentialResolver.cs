using System.Security.Claims;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQCredentialResolver(
    ResourceGraphModel graphModel,
    ResourceGraphResolver graphResolver,
    IResourcePermissionGrantReader grantReader,
    IRabbitMQAccessReconciler accessReconciler,
    IRabbitMQPrincipalCredentialProvider credentialProvider,
    IOptions<RabbitMQManagementAccessOptions> options,
    IResourceEventSink? events = null)
{
    private const string CredentialResolvedEvent = "credential.resolved";
    private const string CredentialRequestDeniedEvent = "credential.request.denied";
    private const string CredentialRequestFailedEvent = "credential.request.failed";

    private readonly RabbitMQManagementAccessOptions options = options.Value;

    public async Task<ResolveRabbitMQCredentialResponse> ResolveAsync(
        ResolveRabbitMQCredentialRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        var permission = NormalizePermission(request.Permission);
        if (!IsBrokerPermission(permission))
        {
            throw new ArgumentException(
                $"RabbitMQ credentials can only be resolved for broker permissions. Requested permission '{permission}'.",
                nameof(request));
        }

        var subject = ResolveSubject(principal);
        var resource = await ResolveResourceAsync(request.RabbitMQResourceName, cancellationToken);

        if (!ResourcePermissionClaimAuthorization.HasResourcePermission(
                principal,
                resource.EffectiveResourceId,
                permission))
        {
            AppendDenied(resource.EffectiveResourceId, subject, permission, "The token does not include the requested RabbitMQ resource permission.");
            throw new UnauthorizedAccessException(
                "The token does not include the requested RabbitMQ resource permission.");
        }

        var grants = grantReader.GetPermissionGrants();
        var matchingGrant = grants.FirstOrDefault(grant =>
            MatchesPrincipal(grant.Principal, subject) &&
            Matches(grant.TargetResourceId, resource.EffectiveResourceId) &&
            MatchesPermission(grant.Permission, permission));
        if (matchingGrant is null)
        {
            AppendDenied(resource.EffectiveResourceId, subject, permission, "No matching RabbitMQ permission grant is declared for this identity.");
            throw new UnauthorizedAccessException(
                "No matching RabbitMQ permission grant is declared for this identity.");
        }

        var principalGrants = grants
            .Where(grant =>
                MatchesPrincipal(grant.Principal, subject) &&
                Matches(grant.TargetResourceId, resource.EffectiveResourceId) &&
                IsBrokerPermission(grant.Permission))
            .ToArray();

        var diagnostics = await accessReconciler.ReconcileAccessAsync(
            resource,
            principalGrants,
            cancellationToken);
        var error = diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
        if (error is not null)
        {
            AppendFailed(resource.EffectiveResourceId, subject, permission, error.Message);
            throw new InvalidOperationException(
                $"RabbitMQ access reconciliation failed before credential resolution: {error.Message}");
        }

        var credentials = credentialProvider.CreateCredentials(
            resource.EffectiveResourceId,
            matchingGrant.Principal);
        AppendResolved(resource.EffectiveResourceId, subject, permission);

        return new ResolveRabbitMQCredentialResponse(
            credentials.UserName,
            credentials.Password,
            RabbitMQResourceConfiguration.ResolveVirtualHost(resource, options));
    }

    private async Task<Resource> ResolveResourceAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var normalizedName = resourceName.Trim();
        var snapshot = await graphModel.GetSnapshotAsync(cancellationToken);
        var state = snapshot.Resources.FirstOrDefault(resource =>
            Matches(resource.EffectiveResourceId, normalizedName) ||
            Matches(resource.Name, normalizedName));
        if (state is null)
        {
            throw new ArgumentException(
                $"RabbitMQ resource '{normalizedName}' was not found.",
                nameof(resourceName));
        }

        var resolution = graphResolver.ResolveResource(snapshot, state.EffectiveResourceId);
        if (resolution.Resource is null)
        {
            throw new ArgumentException(
                $"RabbitMQ resource '{normalizedName}' could not be resolved.",
                nameof(resourceName));
        }

        if (resolution.Resource.Type.TypeId != RabbitMQResourceTypeProvider.ResourceTypeId)
        {
            throw new ArgumentException(
                $"Resource '{normalizedName}' is not a RabbitMQ resource.",
                nameof(resourceName));
        }

        return resolution.Resource;
    }

    private static string NormalizePermission(string? permission) =>
        string.IsNullOrWhiteSpace(permission)
            ? RabbitMQResourceOperationPermissions.Configure
            : permission.Trim();

    private static string ResolveSubject(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("The RabbitMQ credential request is not authenticated.");
        }

        var subject =
            principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
            principal.FindFirstValue("sub") ??
            principal.Identity.Name;
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new UnauthorizedAccessException("The authenticated principal does not include a subject.");
        }

        return subject.Trim();
    }

    private static bool MatchesPrincipal(
        ResourcePrincipalReference principal,
        string subject) =>
        Matches(principal.Id, subject) ||
        !string.IsNullOrWhiteSpace(principal.SourceResourceId) &&
        Matches(
            CreateResourceIdentityClientId(
                principal.SourceResourceId,
                principal.SourceIdentityName),
            subject);

    private static string CreateResourceIdentityClientId(
        string resourceId,
        string? identityName) =>
        string.IsNullOrWhiteSpace(identityName)
            ? resourceId
            : $"{resourceId}/{identityName}";

    private static bool MatchesPermission(string grantPermission, string requestedPermission) =>
        Matches(grantPermission, requestedPermission) ||
        Matches(grantPermission, CloudShellPermissions.All);

    private static bool IsBrokerPermission(string permission) =>
        string.Equals(permission, RabbitMQResourceOperationPermissions.Publish, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(permission, RabbitMQResourceOperationPermissions.Consume, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(permission, RabbitMQResourceOperationPermissions.Configure, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private void AppendDenied(
        string resourceId,
        string subject,
        string permission,
        string message) =>
        Append(resourceId, CredentialRequestDeniedEvent, subject, permission, message, ResourceSignalSeverity.Warning);

    private void AppendFailed(
        string resourceId,
        string subject,
        string permission,
        string message) =>
        Append(resourceId, CredentialRequestFailedEvent, subject, permission, message, ResourceSignalSeverity.Error);

    private void AppendResolved(
        string resourceId,
        string subject,
        string permission) =>
        Append(
            resourceId,
            CredentialResolvedEvent,
            subject,
            permission,
            $"Resolved RabbitMQ credentials for resource identity '{subject}'.",
            ResourceSignalSeverity.Info);

    private void Append(
        string resourceId,
        string eventName,
        string subject,
        string permission,
        string message,
        ResourceSignalSeverity severity)
    {
        events?.Append(new ResourceEvent(
            resourceId,
            ResourceEventTypes.Events.Provider.ForEvent(
                RabbitMQResourceTypeProvider.ProviderId,
                eventName),
            $"{message} Permission: {permission}.",
            DateTimeOffset.UtcNow,
            subject,
            severity).WithCurrentTraceContext());
    }
}
