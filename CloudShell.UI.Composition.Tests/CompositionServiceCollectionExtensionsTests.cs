using CloudShell.UI.Composition.Blazor;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.UI.Composition.Tests;

public sealed class CompositionServiceCollectionExtensionsTests
{
    private static readonly CompositionModuleId HostModule = CompositionModuleId.Host;
    private static readonly CompositionModuleId ExtensionModule = CompositionModuleId.Create("extension");
    private static readonly PageId WorkspacePage = PageId.Create("workspace");
    private static readonly SectionOutletId WorkspaceOutlet = SectionOutletId.Create(WorkspacePage, "main");
    private static readonly SectionId OverviewSection = SectionId.Create(WorkspaceOutlet, "overview");
    private static readonly SectionId ExtensionSection = SectionId.Create(WorkspaceOutlet, "extension");

    [Fact]
    public void AddCloudShellUiComposition_AssemblesRegistryFromRegisteredModules()
    {
        var services = new ServiceCollection();

        services.AddCloudShellUiCompositionModule(HostModule, module =>
        {
            module
                .AddPage(WorkspacePage, "Workspace", "/")
                .AddSections(WorkspaceOutlet, isExtendable: true)
                .AddSection<OverviewSectionComponent>(
                    OverviewSection,
                    "Overview",
                    10);
        });
        services.AddCloudShellUiCompositionModule(ExtensionModule, module =>
        {
            module
                .GetSections(WorkspacePage, WorkspaceOutlet)
                .AddSection<ExtensionSectionComponent>(
                    ExtensionSection,
                    "Extension",
                    20);
        });
        services.AddCloudShellUiComposition();

        var provider = CreateProvider(services);

        var host = provider.GetRequiredService<CompositionEngineHost>();
        var registry = provider.GetRequiredService<CompositionRegistry>();

        Assert.Same(host.Registry, registry);
        Assert.Equal(2, host.Modules.Count);

        var sections = registry.GetSectionProjections(WorkspacePage, WorkspaceOutlet);
        Assert.Collection(
            sections,
            section =>
            {
                Assert.Equal(HostModule, section.ModuleId);
                Assert.Equal(OverviewSection, section.Section.Id);
            },
            section =>
            {
                Assert.Equal(ExtensionModule, section.ModuleId);
                Assert.Equal(ExtensionSection, section.Section.Id);
            });
    }

    [Fact]
    public void AddCloudShellUiComposition_WithModuleCollectionRegistersModules()
    {
        var hostModule = CompositionModule.Create(HostModule, module =>
        {
            module
                .AddPage(WorkspacePage, "Workspace", "/")
                .AddSections(WorkspaceOutlet)
                .AddSection<OverviewSectionComponent>(
                    OverviewSection,
                    "Overview",
                    10);
        });
        var services = new ServiceCollection();

        services.AddCloudShellUiComposition([hostModule]);

        var provider = CreateProvider(services);

        var registry = provider.GetRequiredService<CompositionRegistry>();

        Assert.NotNull(registry.GetPage(WorkspacePage));
        var section = Assert.Single(registry.GetSections(WorkspacePage, WorkspaceOutlet));
        Assert.Equal(OverviewSection, section.Id);
    }

    private sealed class OverviewSectionComponent;

    private sealed class ExtensionSectionComponent;

    private static TestServiceProvider CreateProvider(IServiceCollection services)
    {
        var modules = services
            .Where(descriptor => descriptor.ServiceType == typeof(CompositionModule))
            .Select(descriptor => Assert.IsType<CompositionModule>(descriptor.ImplementationInstance))
            .ToArray();
        var hostFactory = services.Single(descriptor => descriptor.ServiceType == typeof(CompositionEngineHost))
            .ImplementationFactory;
        var registryFactory = services.Single(descriptor => descriptor.ServiceType == typeof(CompositionRegistry))
            .ImplementationFactory;

        Assert.NotNull(hostFactory);
        Assert.NotNull(registryFactory);

        var provider = new TestServiceProvider();
        provider.Add(typeof(IEnumerable<CompositionModule>), modules);
        provider.Add(typeof(CompositionEngineHost), hostFactory(provider));
        provider.Add(typeof(CompositionRegistry), registryFactory(provider));

        return provider;
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = [];

        public void Add(Type serviceType, object service) =>
            _services.Add(serviceType, service);

        public object? GetService(Type serviceType) =>
            _services.GetValueOrDefault(serviceType);
    }
}
