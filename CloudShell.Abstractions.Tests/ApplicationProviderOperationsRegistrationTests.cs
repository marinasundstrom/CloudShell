using CloudShell.Abstractions.Hosting;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationProviderOperationsRegistrationTests
{
    [Fact]
    public void AddApplicationProvider_RegistersUiFacingOperationContracts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Path.GetTempPath()));
        services
            .AddControlPlane()
            .AddApplicationProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var applicationService = serviceProvider.GetRequiredService<ApplicationResourceService>();

        Assert.IsType<ApplicationResourceConfigurationOperations>(
            serviceProvider.GetRequiredService<IApplicationResourceConfigurationOperations>());
        Assert.IsType<ApplicationResourceRegistrationOperations>(
            serviceProvider.GetRequiredService<IApplicationResourceRegistrationOperations>());
        Assert.Same(
            applicationService,
            serviceProvider.GetRequiredService<IApplicationResourceRunningStateOperations>());
        Assert.IsType<ApplicationResourceDefinitionSource>(
            serviceProvider.GetRequiredService<IApplicationResourceDefinitionSource>());
        Assert.IsType<ApplicationContainerHistoryService>(
            serviceProvider.GetRequiredService<IContainerApplicationHistoryOperations>());
        Assert.IsType<SqlServerDatabaseInspectionService>(
            serviceProvider.GetRequiredService<ISqlServerDatabaseInspectionOperations>());
        Assert.IsType<SqlServerCredentialResolutionService>(
            serviceProvider.GetRequiredService<ISqlServerCredentialResolutionOperations>());
        Assert.IsType<SqlServerGrantStatusService>(
            serviceProvider.GetRequiredService<ISqlServerApplicationResourceProviderOperations>());
        Assert.IsType<ApplicationResourceDeclarationOperations>(
            serviceProvider.GetRequiredService<IApplicationResourceDeclarationOperations>());
        Assert.IsType<SqlServerDatabaseReconciliationService>(
            serviceProvider.GetRequiredService<SqlServerDatabaseReconciliationService>());
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(contentRootPath);
    }
}
