using CloudShell.Abstractions.Hosting;
using CloudShell.Host.Components;
using CloudShell.Host.Hosting;
using CloudShell.UiExtensionHost;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddCloudShellUi()
    .AddExtension<SampleWorkspaceExtension>();

var app = builder.Build();

await app.UseCloudShellUiAsync();
app.MapCloudShellUi<App>();

app.Run();
