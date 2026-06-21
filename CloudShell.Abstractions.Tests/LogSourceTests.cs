using CloudShell.Abstractions.Logs;

namespace CloudShell.Abstractions.Tests;

public sealed class LogSourceTests
{
    [Fact]
    public void LogDescriptor_ProjectsToLogSourceWithSourceMetadata()
    {
        var descriptor = new LogDescriptor(
            "application:test:logs",
            "Console logs",
            "Applications",
            "Test API",
            LogSourceKind.Resource,
            ResourceId: "application:test",
            SupportsStreaming: true,
            Description: "Application stdout and stderr.",
            Kind: ResourceLogSourceKind.ProcessOutput,
            Format: LogFormat.JsonConsole,
            Storage: LogStorage.InMemory,
            Capabilities: LogSourceCapabilities.Read | LogSourceCapabilities.StructuredFields,
            ProducerResourceId: "application:test",
            Origin: ResourceLogSourceOrigin.Programmatic,
            Configuration: new LogSourceConfiguration(
                IsConfigurable: true,
                SchemaId: "application.processOutput"),
            Purpose: ResourceLogSourcePurpose.Custom);

        var source = descriptor.ToLogSource();

        Assert.Equal(descriptor.Id, source.Id);
        Assert.Equal(ResourceLogSourceKind.ProcessOutput, source.Kind);
        Assert.Equal(LogFormat.JsonConsole, source.Format);
        Assert.Equal(LogStorageKind.InMemory, source.Storage.Kind);
        Assert.True(source.SupportsStreaming);
        Assert.True(source.Capabilities.HasFlag(LogSourceCapabilities.StructuredFields));
        Assert.Equal("application:test", source.ResourceId);
        Assert.Equal("application:test", source.ProducerResourceId);
        Assert.Equal(ResourceLogSourceOrigin.Programmatic, source.Origin);
        Assert.True(source.Configuration.IsConfigurable);
        Assert.Equal("application.processOutput", source.Configuration.SchemaId);
        Assert.Equal(ResourceLogSourcePurpose.Custom, source.Purpose);
    }
}
