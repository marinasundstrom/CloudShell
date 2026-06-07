using CloudShell.Abstractions.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.UiExtensionHost;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddCloudShellUi()
    .AddExtension<SampleWorkspaceExtension>();

var app = builder.Build();

await app.UseCloudShellUiAsync();
app.MapCloudShellUi<App>();

app.Run();
