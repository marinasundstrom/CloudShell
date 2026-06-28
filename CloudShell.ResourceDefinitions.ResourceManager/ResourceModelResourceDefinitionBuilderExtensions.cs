using System.Runtime.CompilerServices;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public static class ResourceModelResourceDefinitionBuilderExtensions
{
    private static readonly ConditionalWeakTable<IResourceDefinitionBuilder, ResourceModelDeclarationMetadata>
        DeclarationMetadata = new();

    public static TBuilder WithResourceGroup<TBuilder>(
        this TBuilder builder,
        string? resourceGroupId)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetMetadata(builder).ResourceGroupId = NormalizeOptional(resourceGroupId);
        return builder;
    }

    public static TBuilder WithAutoStart<TBuilder>(
        this TBuilder builder,
        bool autoStart = true)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetMetadata(builder).AutoStart = autoStart;
        return builder;
    }

    public static TBuilder WithDependencyAutoStart<TBuilder>(
        this TBuilder builder,
        bool autoStart = true)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetMetadata(builder).DependencyAutoStart = autoStart;
        return builder;
    }

    public static IControlPlaneBuilder AddResourceGroup(
        this IControlPlaneBuilder builder,
        string id,
        string name,
        string description = "")
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.Services)
            .AddResourceGroup(id, name, description);
        return builder;
    }

    internal static ResourceModelDeclarationMetadata GetDeclarationMetadata(
        IResourceDefinitionBuilder builder) =>
        DeclarationMetadata.TryGetValue(builder, out var metadata)
            ? metadata
            : ResourceModelDeclarationMetadata.Empty;

    internal static ResourceDeclarationStore GetOrAddDeclarationStore(IServiceCollection services)
    {
        var declarations = services
            .Where(descriptor => descriptor.ServiceType == typeof(ResourceDeclarationStore))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ResourceDeclarationStore>()
            .SingleOrDefault();

        if (declarations is not null)
        {
            return declarations;
        }

        declarations = new ResourceDeclarationStore();
        services.AddSingleton(declarations);
        return declarations;
    }

    private static ResourceModelDeclarationMetadata GetMetadata(IResourceDefinitionBuilder builder) =>
        DeclarationMetadata.GetValue(builder, _ => new ResourceModelDeclarationMetadata());

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class ResourceModelDeclarationMetadata
{
    public static ResourceModelDeclarationMetadata Empty { get; } = new();

    public string? ResourceGroupId { get; set; }

    public bool? AutoStart { get; set; }

    public bool? DependencyAutoStart { get; set; }
}
