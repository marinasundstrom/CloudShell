using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Hosting;

public interface ICloudShellBuilder
{
    IServiceCollection Services { get; }
}
