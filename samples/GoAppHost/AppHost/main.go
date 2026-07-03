package main

import (
	"os"
	"path/filepath"

	"github.com/cloudshell/launcher-go/cloudshell"
)

func main() {
	repoRoot := findRepositoryRoot(mustGetwd())
	sampleRoot := filepath.Join(repoRoot, "samples", "GoAppHost")
	goAppRoot := filepath.Join(repoRoot, "samples", "GoApp", "App")

	options := cloudshell.DefaultLauncherOptions()
	options.CLIProject = pathFromEnv("CLOUDSHELL_CLI_PROJECT", filepath.Join(repoRoot, "CloudShell.Cli", "CloudShell.Cli.csproj"))
	options.HostProject = pathFromEnv("CLOUDSHELL_HOST_PROJECT", filepath.Join(repoRoot, "CloudShell.LocalDevelopmentHost", "CloudShell.LocalDevelopmentHost.csproj"))
	options.StateDir = pathFromEnv("CLOUDSHELL_STATE_DIR", filepath.Join(sampleRoot, ".cloudshell"))
	options.DataDir = pathFromEnv("CLOUDSHELL_DATA_DIR", options.StateDir)
	options.TemplatePath = pathFromEnv("CLOUDSHELL_TEMPLATE_PATH", filepath.Join(options.StateDir, "resources.json"))
	options.ControlPlaneURL = envOrDefault("CLOUDSHELL_CONTROL_PLANE_URL", "http://127.0.0.1:5101")
	options.HostURL = options.ControlPlaneURL
	options.WorkingDirectory = repoRoot

	os.Exit(buildTemplate(goAppRoot).RunWithOptions(os.Args[1:], options))
}

func buildTemplate(goAppRoot string) *cloudshell.App {
	app := cloudshell.NewApp("go-app-host").
		WithEnvironmentID("local").
		WithMetadata("cloudshell.source", "go").
		WithMetadata("cloudshell.sample", "GoAppHost")

	hostNetwork := app.AddNetwork("host").
		WithResourceID("network:host").
		WithDisplayName("Host network").
		WithNetworkKind("Host").
		WithHostReadiness("hostReady")

	settings := app.AddConfigurationStore("go-launcher-settings").
		WithDisplayName("Go Launcher Settings").
		WithEndpoint("http://localhost:5106").
		WithSetting("Sample--Message", "Hello from Go launcher seed")

	secrets := app.AddSecretsVault("go-launcher-secrets").
		WithDisplayName("Go Launcher Secrets").
		WithEndpoint("http://localhost:6106").
		WithSecret("Sample--ApiKey", "go-launcher-secret", "v1")

	app.AddGoApp("go-launcher-api", goAppRoot).
		WithDisplayName("Go Launcher API").
		WithServiceDiscovery().
		WithEnvironmentVariable("PORT", "5187").
		WithEnvironmentVariable("OTEL_SERVICE_NAME", "go-launcher-api").
		WithConfigurationEntry("Sample__Message", settings.Entry("Sample--Message")).
		WithSecret("Sample__ApiKey", secrets.Secret("Sample--ApiKey")).
		WithReference(settings).
		WithReference(secrets).
		DependsOn(settings).
		DependsOn(secrets).
		WithHttpEndpoint("localhost", 5187, 5187, hostNetwork).
		WithHttpHealthCheck("/healthz").
		WithHttpLivenessCheck("/alive").
		WithDefaultConsoleLogSource()

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
