package cloudshell

import (
	"encoding/json"
	"reflect"
	"testing"
)

func TestBuildsGoAppTemplate(t *testing.T) {
	app := NewApp("go-test").
		WithEnvironmentID("local").
		WithMetadata("cloudshell.source", "go")

	network := app.AddNetwork("host").
		WithResourceID("network:host").
		WithDisplayName("Host network").
		WithNetworkKind("Host").
		WithHostReadiness("hostReady")

	settings := app.AddConfigurationStore("settings").
		WithDisplayName("Settings").
		WithEndpoint("http://localhost:5105").
		WithSetting("Sample--Message", "Hello from Go")

	secrets := app.AddSecretsVault("secrets").
		WithDisplayName("Secrets").
		WithEndpoint("http://localhost:6105").
		WithSecret("Sample--ApiKey", "go-secret", "v1")

	app.AddGoApp("api", "samples/GoApp/App").
		WithDisplayName("Go API").
		WithServiceDiscovery().
		WithEnvironmentVariable("PORT", "5186").
		WithConfigurationEntry("Sample__Message", settings.Entry("Sample--Message")).
		WithSecret("Sample__ApiKey", secrets.Secret("Sample--ApiKey")).
		WithReference(settings).
		WithReference(secrets).
		DependsOn(settings).
		DependsOn(secrets).
		WithHttpEndpoint("localhost", 5186, 5186, network).
		WithHttpHealthCheck("/ready").
		WithHttpLivenessCheck("/live").
		WithDefaultConsoleLogSource()

	jsonValue, err := app.ToJSON()
	if err != nil {
		t.Fatal(err)
	}

	var template ResourceTemplate
	if err := json.Unmarshal([]byte(jsonValue), &template); err != nil {
		t.Fatal(err)
	}

	if template.Name != "go-test" {
		t.Fatalf("unexpected template name: %s", template.Name)
	}
	if template.EnvironmentID != "local" {
		t.Fatalf("unexpected environment id: %s", template.EnvironmentID)
	}
	if template.Metadata["cloudshell.source"] != "go" {
		t.Fatalf("unexpected metadata: %#v", template.Metadata)
	}

	var goResource map[string]any
	for _, resource := range template.Resources {
		if resource["name"] == "api" {
			goResource = resource
			break
		}
	}
	if goResource == nil {
		t.Fatal("Go app resource was not emitted")
	}

	assertEqual(t, "application.go-app", goResource["type"])
	assertEqual(t, "applications.go-app", goResource["providerId"])
	assertEqual(t, "application.go-app:api", goResource["resourceId"])
	assertNestedEqual(t, "go.command", "go", goResource, "go", "command")
	assertNestedEqual(t, "go.packagePath", ".", goResource, "go", "packagePath")
	assertNestedEqual(t, "project.path", "samples/GoApp/App", goResource, "project", "path")
	assertNestedEqual(t, "project.serviceDiscoveryName", "api", goResource, "project", "serviceDiscoveryName")

	project := goResource["project"].(map[string]any)
	environment := project["environmentVariables"].(map[string]any)
	assertNestedEqual(t, "env literal", "5186", environment, "PORT", "value")
	assertNestedEqual(t, "env configuration ref", "configuration.store:settings", environment, "Sample__Message", "configurationEntryRef", "storeResourceId")
	assertNestedEqual(t, "env secret ref", "secrets.vault:secrets", environment, "Sample__ApiKey", "secretRef", "vaultResourceId")

	health := goResource["health"].(map[string]any)
	checks := health["checks"].([]any)
	if len(checks) != 2 {
		t.Fatalf("expected two health checks, got %d", len(checks))
	}

	logs := goResource["logs"].(map[string]any)
	sources := logs["sources"].([]any)
	if len(sources) != 1 {
		t.Fatalf("expected one log source, got %d", len(sources))
	}
}

func TestRejectsDuplicateResourceIDs(t *testing.T) {
	app := NewApp("duplicates")
	app.AddNetwork("one").WithResourceID("network:shared")

	defer func() {
		if recover() == nil {
			t.Fatal("expected duplicate resource id panic")
		}
	}()

	app.AddNetwork("two").WithResourceID("network:shared")
}

func TestBuildTemplateApplyCommand(t *testing.T) {
	options := DefaultLauncherOptions()
	options.CLIProject = "CloudShell.Cli/CloudShell.Cli.csproj"
	options.ControlPlaneURL = "http://127.0.0.1:5100"
	options.StateDir = ".cloudshell"
	options.HostProject = "CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj"
	options.DataDir = ".cloudshell"
	options.HostURL = "http://127.0.0.1:5100"
	options.NoBuild = true

	command, args := buildTemplateApplyCommand(".cloudshell/resources.json", options, false)
	assertEqual(t, "dotnet", command)
	assertDeepEqual(t, []string{
		"run",
		"--project",
		"CloudShell.Cli/CloudShell.Cli.csproj",
		"--",
		"template",
		"apply",
		".cloudshell/resources.json",
		"--control-plane",
		"http://127.0.0.1:5100",
		"--state-dir",
		".cloudshell",
		"--host-project",
		"CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj",
		"--data-dir",
		".cloudshell",
		"--url",
		"http://127.0.0.1:5100",
		"--timeout-seconds",
		"60",
		"--mode",
		"create-or-update",
		"--no-build",
	}, args)
}

func TestBuildHostRunCommand(t *testing.T) {
	options := DefaultLauncherOptions()
	options.HostProject = "CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj"
	options.DataDir = ".cloudshell"
	options.NoBuild = true

	command, args, err := buildHostRunCommand(options, "http://127.0.0.1:5100")
	if err != nil {
		t.Fatal(err)
	}

	assertEqual(t, "dotnet", command)
	assertDeepEqual(t, []string{
		"run",
		"--project",
		"CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj",
		"--no-build",
		"--",
		"--urls",
		"http://127.0.0.1:5100",
		"--CloudShell:DataDirectory",
		".cloudshell",
	}, args)
}

func TestFormatHostURLMessage(t *testing.T) {
	assertEqual(t, "CloudShell UI: http://127.0.0.1:5100", FormatHostURLMessage("http://127.0.0.1:5100/"))
}

func assertNestedEqual(t *testing.T, name string, expected any, document map[string]any, path ...string) {
	t.Helper()

	var current any = document
	for _, segment := range path {
		currentMap, ok := current.(map[string]any)
		if !ok {
			t.Fatalf("%s: %v is not an object", name, current)
		}
		current = currentMap[segment]
	}

	assertEqual(t, expected, current)
}

func assertEqual(t *testing.T, expected any, actual any) {
	t.Helper()

	if expected != actual {
		t.Fatalf("expected %v, got %v", expected, actual)
	}
}

func assertDeepEqual(t *testing.T, expected any, actual any) {
	t.Helper()

	if !reflect.DeepEqual(expected, actual) {
		t.Fatalf("expected %#v, got %#v", expected, actual)
	}
}
