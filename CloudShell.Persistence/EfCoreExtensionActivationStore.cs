using CloudShell.Abstractions.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CloudShell.Persistence;

public sealed class EfCoreExtensionActivationStore(
    IDbContextFactory<CloudShellDbContext> contextFactory) : ICloudShellExtensionActivationStore
{
    public IReadOnlyDictionary<string, CloudShellExtensionActivationState> GetActivationStates()
    {
        using var context = contextFactory.CreateDbContext();

        return context.ExtensionActivations
            .AsNoTracking()
            .ToArray()
            .ToDictionary(
                activation => activation.ExtensionId,
                activation => ParseState(activation.State),
                StringComparer.OrdinalIgnoreCase);
    }

    public CloudShellExtensionActivationState? GetActivationState(string extensionId)
    {
        using var context = contextFactory.CreateDbContext();
        var state = context.ExtensionActivations
            .AsNoTracking()
            .Where(activation => activation.ExtensionId == extensionId)
            .Select(activation => activation.State)
            .SingleOrDefault();

        return state is null ? null : ParseState(state);
    }

    public async Task SetActivationStateAsync(
        string extensionId,
        CloudShellExtensionActivationState state,
        string? updatedBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var activation = await context.ExtensionActivations
            .SingleOrDefaultAsync(item => item.ExtensionId == extensionId, cancellationToken);

        if (activation is null)
        {
            context.ExtensionActivations.Add(new ExtensionActivationEntity
            {
                ExtensionId = extensionId,
                State = state.ToString(),
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = updatedBy
            });
        }
        else
        {
            activation.State = state.ToString();
            activation.UpdatedAt = DateTimeOffset.UtcNow;
            activation.UpdatedBy = updatedBy;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static CloudShellExtensionActivationState ParseState(string state) =>
        Enum.TryParse<CloudShellExtensionActivationState>(state, ignoreCase: true, out var parsed)
            ? parsed
            : CloudShellExtensionActivationState.Enabled;
}
