package main

import (
	"os"
	"path/filepath"

	"github.com/cloudshell/launcher-go/cloudshell"
)

func main() {
	repoRoot := findRepositoryRoot(mustGetwd())
	sampleRoot := filepath.Join(repoRoot, "samples", "GoContainerApp")
	goAppRoot := filepath.Join(sampleRoot, "App")

	options := cloudshell.DefaultLauncherOptions()
	options.CLIProject = pathFromEnv("CLOUDSHELL_CLI_PROJECT", filepath.Join(repoRoot, "CloudShell.Cli", "CloudShell.Cli.csproj"))
	options.HostProject = pathFromEnv("CLOUDSHELL_HOST_PROJECT", filepath.Join(repoRoot, "CloudShell.LocalDevelopmentHost", "CloudShell.LocalDevelopmentHost.csproj"))
	options.StateDir = pathFromEnv("CLOUDSHELL_STATE_DIR", filepath.Join(sampleRoot, ".cloudshell"))
	options.DataDir = pathFromEnv("CLOUDSHELL_DATA_DIR", options.StateDir)
	options.TemplatePath = pathFromEnv("CLOUDSHELL_TEMPLATE_PATH", filepath.Join(options.StateDir, "resources.json"))
	options.ControlPlaneURL = envOrDefault("CLOUDSHELL_CONTROL_PLANE_URL", "http://127.0.0.1:5108")
	options.HostURL = options.ControlPlaneURL
	options.WorkingDirectory = repoRoot

	os.Exit(buildTemplate(repoRoot, goAppRoot).RunWithOptions(os.Args[1:], options))
}

func buildTemplate(repoRoot string, goAppRoot string) *cloudshell.App {
	app := cloudshell.NewApp("go-container-app").
		WithEnvironmentID("local").
		WithMetadata("cloudshell.source", "go").
		WithMetadata("cloudshell.sample", "GoContainerApp")

	hostNetwork := app.AddNetwork("host").
		WithResourceID("network:host").
		WithDisplayName("Host network").
		WithNetworkKind("Host").
		WithHostReadiness("hostReady")

	settings := app.AddConfigurationStore("go-container-settings").
		WithDisplayName("Go Container Settings").
		WithEndpoint("http://localhost:5109").
		WithSeed(func(seed *cloudshell.ConfigurationStoreSeed) {
			seed.Setting("Sample--Message", "Hello from Go container app configuration")
			seed.Setting("Sample--Mode", "container")
		})

	secrets := app.AddSecretsVault("go-container-secrets").
		WithDisplayName("Go Container Secrets").
		WithEndpoint("http://localhost:6109").
		WithSeed(func(seed *cloudshell.SecretsVaultSeed) {
			seed.Secret("Sample--ApiKey", "go-container-secret", "v1")
		})

	api := app.AddGoApp("go-container-api", goAppRoot).
		WithDisplayName("Go Container API").
		WithServiceDiscovery().
		WithEnvironmentVariable("PORT", "8080").
		WithEnvironmentVariable("OTEL_SERVICE_NAME", "go-container-api").
		WithReference(settings).
		WithReference(secrets).
		DependsOn(settings).
		DependsOn(secrets).
		WithHttpEndpoint("localhost", 5188, 8080, hostNetwork).
		WithHttpHealthCheck("/healthz").
		WithHttpLivenessCheck("/alive").
		WithDefaultConsoleLogSource().
		AsContainerApp(cloudshell.ContainerAppOptions{
			Tag:          "dev",
			BuildContext: repoRoot,
			Dockerfile:   filepath.Join("samples", "GoContainerApp", "App", "Dockerfile"),
			Replicas:     2,
		}).
		RequireIdentity("go-container-api").
		ProvisionIdentityOnStartup()

	settings.AllowResourceIdentity(
		api,
		"CloudShell.Configuration/stores/settings/read/action",
		cloudshell.ResourceIdentityGrantOptions{IdentityName: "go-container-api"})
	secrets.AllowResourceIdentity(
		api,
		"CloudShell.Secrets/vaults/secrets/read/action",
		cloudshell.ResourceIdentityGrantOptions{IdentityName: "go-container-api"})

	return app
}

func mustGetwd() string {
	workingDirectory, err := os.Getwd()
	if err != nil {
		panic(err)
	}

	return workingDirectory
}

func findRepositoryRoot(start string) string {
	directory := start
	for {
		if _, err := os.Stat(filepath.Join(directory, "CloudShell.slnx")); err == nil {
			return directory
		}

		parent := filepath.Dir(directory)
		if parent == directory {
			panic("could not find CloudShell.slnx from " + start)
		}

		directory = parent
	}
}

func pathFromEnv(name string, fallback string) string {
	value := os.Getenv(name)
	if value == "" {
		return fallback
	}

	return value
}

func envOrDefault(name string, fallback string) string {
	value := os.Getenv(name)
	if value == "" {
		return fallback
	}

	return value
}
