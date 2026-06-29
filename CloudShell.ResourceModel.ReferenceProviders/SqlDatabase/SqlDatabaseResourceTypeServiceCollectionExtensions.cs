using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceModel.ReferenceProviders;

public static class SqlDatabaseResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddSqlDatabaseResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == SqlDatabaseResourceTypeProvider.ClassId))
        {
            services.AddSingleton(SqlDatabaseResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, SqlDatabaseResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, SqlDatabaseResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, SqlDatabaseResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, SqlDatabaseGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceGraphDependencyProvider, SqlDatabaseGraphDependencyProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IResourceOperationProvider, SqlDatabaseEnsureCreatedOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IResourceOperationProjector, SqlDatabaseEnsureCreatedOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, SqlDatabaseResourceProjectionProvider>());
        services.TryAddScoped<
            ISqlDatabaseServerResolver,
            ContextSqlDatabaseServerResolver>();
        services.TryAddSingleton<
            ISqlDatabaseCreationHandler,
            NoopSqlDatabaseCreationHandler>();

        return services;
    }
}
