package cloudshell

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"reflect"
	"runtime"
	"sort"
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
		WithSeed(func(seed *ConfigurationStoreSeed) {
			seed.Setting("Sample--Message", "Hello from Go")
		})

	secrets := app.AddSecretsVault("secrets").
		WithDisplayName("Secrets").
		WithEndpoint("http://localhost:6105").
		WithSeed(func(seed *SecretsVaultSeed) {
			seed.Secret("Sample--ApiKey", "go-secret", "v1").
				Certificate("ApiTls", "go-certificate", "application/x-pem-file", "v1")
		})

	api := app.AddGoApp("api", "samples/GoApp/App").
		WithDisplayName("Go API").
		WithServiceDiscovery().
		WithEnvironmentVariable("PORT", "5186").
		WithConfigurationSetting("Sample__Message", settings.Setting("Sample--Message")).
		WithSecret("Sample__ApiKey", secrets.Secret("Sample--ApiKey")).
		WithReference(settings).
		WithReference(secrets).
		DependsOn(settings).
		DependsOn(secrets).
		WithHttpEndpoint("localhost", 5186, 5186, network).
		WithHttpHealthCheck("/ready").
		WithHttpLivenessCheck("/live").
		WithDefaultConsoleLogSource().
		RequireIdentity("api").
		ProvisionIdentityOnStartup()

	settings.AllowResourceIdentity(
		api,
		"CloudShell.Configuration/stores/settings/read/action",
		ResourceIdentityGrantOptions{IdentityName: "api"})
	secrets.AllowResourceIdentity(
		api,
		"CloudShell.Secrets/vaults/secrets/read/action",
		ResourceIdentityGrantOptions{IdentityName: "api"})

	app.AddLoadBalancer("edge").
		WithDisplayName("Edge").
		WithProvider("traefik").
		UseHost(network).
		ExposeHTTPS(secrets.Certificate("ApiTls"), 4443).
		MapHost("api.local", api, 5186, "https")

	jsonValue, err := app.ToJSON()
	if err != nil {
		t.Fatal(err)
	}

	var template ResourceTemplate
	if err := json.Unmarshal([]byte(jsonValue), &template); err != nil {
		t.Fatal(err)
	}
	assertMatchesParityFixture(t, "go-app-parity.json", jsonValue)

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

	var loadBalancerResource map[string]any
	for _, resource := range template.Resources {
		if resource["name"] == "edge" {
			loadBalancerResource = resource
			break
		}
	}
	if loadBalancerResource == nil {
		t.Fatal("Load balancer resource was not emitted")
	}

	var secretsResource map[string]any
	for _, resource := range template.Resources {
		if resource["name"] == "secrets" {
			secretsResource = resource
			break
		}
	}
	if secretsResource == nil {
		t.Fatal("Secrets Vault resource was not emitted")
	}

	seed := secretsResource["seed"].(map[string]any)
	certificates := seed["certificates"].([]any)
	if len(certificates) != 1 {
		t.Fatalf("expected one certificate seed, got %d", len(certificates))
	}
	certificate := certificates[0].(map[string]any)
	assertEqual(t, "ApiTls", certificate["name"])
	assertEqual(t, "go-certificate", certificate["value"])
	assertEqual(t, "v1", certificate["version"])
	assertEqual(t, "application/x-pem-file", certificate["contentType"])

	assertEqual(t, "application.go-app", goResource["type"])
	assertEqual(t, "applications.go-app", goResource["providerId"])
	assertEqual(t, "application.go-app:api", goResource["resourceId"])
	assertNestedEqual(t, "identity kind", "required", goResource, "attributes", "identity.kind")
	assertNestedEqual(t, "identity name", "api", goResource, "attributes", "identity.name")
	assertNestedEqual(t, "identity provision", true, goResource, "attributes", "identity.provisionOnStartup")
	settingsAttributes := settingsResource(t, template)["attributes"].(map[string]any)
	grants := settingsAttributes["access.grants"].([]any)
	grant := grants[0].(map[string]any)
	assertEqual(t, "CloudShell.Configuration/stores/settings/read/action", grant["permission"])
	principal := grant["principal"].(map[string]any)
	assertEqual(t, "application.go-app:api/identities/api", principal["id"])
	assertEqual(t, "go", goResource["command"])
	assertEqual(t, ".", goResource["packagePath"])
	if _, ok := goResource["go"]; ok {
		t.Fatal("expected Go command attributes at the resource root")
	}
	assertNestedEqual(t, "project.path", "samples/GoApp/App", goResource, "project", "path")
	assertNestedEqual(t, "project.serviceDiscoveryName", "api", goResource, "project", "serviceDiscoveryName")

	project := goResource["project"].(map[string]any)
	environment := project["environmentVariables"].(map[string]any)
	assertNestedEqual(t, "env literal", "5186", environment, "PORT", "value")
	assertNestedEqual(t, "env configuration ref", "configuration.store:settings", environment, "Sample__Message", "configurationSettingRef", "storeResourceId")
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

	assertEqual(t, "cloudshell.loadBalancer", loadBalancerResource["type"])
	assertEqual(t, "cloudshell.load-balancer", loadBalancerResource["providerId"])
	loadBalancer := loadBalancerResource["loadBalancer"].(map[string]any)
	assertEqual(t, "traefik", loadBalancer["provider"])
	assertEqual(t, "network:host", loadBalancer["hostResourceId"])

	entrypoints := loadBalancer["entrypointDefinitions"].([]any)
	if len(entrypoints) != 1 {
		t.Fatalf("expected one load balancer entrypoint, got %d", len(entrypoints))
	}
	entrypoint := entrypoints[0].(map[string]any)
	assertEqual(t, "https", entrypoint["name"])
	assertEqual(t, "Https", entrypoint["protocol"])
	assertEqual(t, float64(4443), entrypoint["port"])
	certificateRef := entrypoint["certificateRef"].(map[string]any)
	assertEqual(t, "secrets.vault:secrets", certificateRef["vaultResourceId"])
	assertEqual(t, "ApiTls", certificateRef["name"])

	routes := loadBalancer["routeDefinitions"].([]any)
	if len(routes) != 1 {
		t.Fatalf("expected one load balancer route, got %d", len(routes))
	}
	route := routes[0].(map[string]any)
	assertEqual(t, "Http", route["kind"])
	assertEqual(t, "https", route["entrypointName"])
	match := route["match"].(map[string]any)
	assertEqual(t, "api.local", match["host"])
	target := route["target"].(map[string]any)
	assertEqual(t, float64(5186), target["port"])
	targetResource := target["resource"].(map[string]any)
	assertEqual(t, "application.go-app:api", targetResource["resourceId"])
	assertEqual(t, "reference", targetResource["relationship"])
}

func settingsResource(t *testing.T, template ResourceTemplate) map[string]any {
	t.Helper()

	for _, resource := range template.Resources {
		if resource["name"] == "settings" {
			return resource
		}
	}

	t.Fatal("Settings resource was not emitted")
	return nil
}

func TestBuildsGoAppAsContainerAppTemplate(t *testing.T) {
	app := NewApp("go-container-test")
	network := app.AddNetwork("host").WithResourceID("network:host")

	app.AddGoApp("api", "samples/GoApp/App").
		WithHttpEndpoint("localhost", 5187, 8080, network).
		AsContainerApp(ContainerAppOptions{
			Tag:        "dev",
			Dockerfile: "Dockerfile",
		})

	jsonValue, err := app.ToJSON()
	if err != nil {
		t.Fatal(err)
	}

	var template ResourceTemplate
	if err := json.Unmarshal([]byte(jsonValue), &template); err != nil {
		t.Fatal(err)
	}

	var resource map[string]any
	for _, candidate := range template.Resources {
		if candidate["name"] == "api" {
			resource = candidate
			break
		}
	}
	if resource == nil {
		t.Fatal("Go container app resource was not emitted")
	}

	assertEqual(t, "application.container-app", resource["type"])
	assertEqual(t, "applications.container-app", resource["providerId"])
	assertEqual(t, "application.container-app:api", resource["resourceId"])
	assertNestedEqual(t, "container.image", "cloudshell-go-api:dev", resource, "container", "image")
	assertNestedEqual(t, "container.buildContext", "samples/GoApp/App", resource, "container", "buildContext")
	assertNestedEqual(t, "container.dockerfile", "Dockerfile", resource, "container", "dockerfile")

	endpoints := resource["endpoints"].([]any)
	if len(endpoints) != 1 {
		t.Fatalf("expected one endpoint request, got %d", len(endpoints))
	}

	project := resource["project"].(map[string]any)
	if _, ok := project["endpointRequests"]; ok {
		t.Fatal("expected endpoint requests to move out of project")
	}

	container := resource["container"].(map[string]any)
	if _, ok := container["endpointRequests"]; ok {
		t.Fatal("expected endpoint requests to move out of container")
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

func assertMatchesParityFixture(t *testing.T, fixtureName string, actualJSON string) {
	t.Helper()

	var actual any
	if err := json.Unmarshal([]byte(actualJSON), &actual); err != nil {
		t.Fatal(err)
	}

	expected := loadParityFixture(t, fixtureName)
	if !reflect.DeepEqual(normalizeTemplate(expected), normalizeTemplate(actual)) {
		t.Fatalf(
			"template did not match fixture %s\nexpected:\n%s\nactual:\n%s",
			fixtureName,
			formatJSON(t, normalizeTemplate(expected)),
			formatJSON(t, normalizeTemplate(actual)))
	}
}

func loadParityFixture(t *testing.T, fixtureName string) any {
	t.Helper()

	_, file, _, ok := runtime.Caller(0)
	if !ok {
		t.Fatal("could not resolve test path")
	}

	path := filepath.Join(filepath.Dir(file), "..", "..", "testdata", fixtureName)
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}

	var value any
	if err := json.Unmarshal(data, &value); err != nil {
		t.Fatal(err)
	}

	return value
}

func normalizeTemplate(value any) any {
	normalized := normalizeValue(value)
	document, ok := normalized.(map[string]any)
	if !ok {
		return normalized
	}

	resources, ok := document["resources"].([]any)
	if !ok {
		return normalized
	}

	sortedResources := append([]any(nil), resources...)
	sort.Slice(sortedResources, func(left int, right int) bool {
		return resourceID(sortedResources[left]) < resourceID(sortedResources[right])
	})
	document["resources"] = sortedResources
	return document
}

func normalizeValue(value any) any {
	switch typed := value.(type) {
	case []any:
		items := make([]any, 0, len(typed))
		for _, item := range typed {
			items = append(items, normalizeValue(item))
		}
		return items
	case map[string]any:
		if _, hasResourceID := typed["resourceId"]; hasResourceID {
			_, hasName := typed["name"]
			_, hasType := typed["type"]
			if !hasName && !hasType {
				return map[string]any{"resourceId": typed["resourceId"]}
			}
		}

		document := make(map[string]any, len(typed))
		for key, item := range typed {
			document[key] = normalizeValue(item)
		}
		return document
	default:
		return value
	}
}

func resourceID(value any) string {
	document, ok := value.(map[string]any)
	if !ok {
		return ""
	}

	id, _ := document["resourceId"].(string)
	return id
}

func formatJSON(t *testing.T, value any) string {
	t.Helper()

	data, err := json.MarshalIndent(value, "", "  ")
	if err != nil {
		return fmt.Sprintf("%#v", value)
	}

	return string(data)
}
