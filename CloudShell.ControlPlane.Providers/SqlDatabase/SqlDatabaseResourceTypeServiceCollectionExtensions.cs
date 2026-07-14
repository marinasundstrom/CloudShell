using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class SqlDatabaseResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseSqlDatabaseResourceProvider(
        this IControlPlaneBuilder builder,
        bool useResourceModelCreationHandler = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSqlDatabaseResourceType();
        if (useResourceModelCreationHandler)
        {
            builder.Services.AddResourceModelSqlDatabaseCreationHandler();
        }

        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddSqlDatabaseResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProviderExecutionDispatcher();

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
            ServiceDescriptor.Singleton<IResourceOperationProvider, SqlDatabaseEnsureCreatedOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, SqlDatabaseEnsureCreatedOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IProviderExecutionHandler, SqlDatabaseEnsureCreatedExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, SqlDatabaseResourceProjectionProvider>());
        services.TryAddSingleton<
            ISqlDatabaseServerResolver,
            ContextSqlDatabaseServerResolver>();
        services.TryAddSingleton<
            ISqlDatabaseCreationHandler,
            NoopSqlDatabaseCreationHandler>();

        return services;
    }

    public static IServiceCollection AddResourceModelSqlDatabaseCreationHandler(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Singleton<
            ISqlDatabaseCreationHandler,
            ResourceModelSqlDatabaseCreationHandler>());

        return services;
    }
}
