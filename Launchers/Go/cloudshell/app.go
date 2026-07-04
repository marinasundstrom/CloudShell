package cloudshell

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

type ResourceReferenceRelationship string

const (
	RelationshipDependsOn ResourceReferenceRelationship = "dependsOn"
	RelationshipReference ResourceReferenceRelationship = "reference"
)

type ResourceReferenceAddressingMode string

const (
	AddressingModeResourceID ResourceReferenceAddressingMode = "resourceId"
)

type TemplateApplyMode string

const (
	ApplyModeCreateOrUpdate TemplateApplyMode = "create-or-update"
	ApplyModeCreateOnly     TemplateApplyMode = "create-only"
	ApplyModeUpdateExisting TemplateApplyMode = "update-existing"
)

type ResourceReference struct {
	ResourceID     string                          `json:"resourceId"`
	Relationship   ResourceReferenceRelationship   `json:"relationship,omitempty"`
	AddressingMode ResourceReferenceAddressingMode `json:"addressingMode,omitempty"`
	TypeID         string                          `json:"typeId,omitempty"`
	ProviderID     string                          `json:"providerId,omitempty"`
}

type ResourceTemplate struct {
	Name          string            `json:"name"`
	Resources     []map[string]any  `json:"resources"`
	EnvironmentID string            `json:"environmentId,omitempty"`
	Metadata      map[string]string `json:"metadata,omitempty"`
}

type ResourceHandle interface {
	Name() string
	Type() string
	ProviderID() string
	ResourceID() string
}

type ResourceBuilder interface {
	ResourceHandle
	build() map[string]any
	setApp(*App)
}

type App struct {
	name          string
	environmentID string
	metadata      map[string]string
	resources     []ResourceBuilder
}

func NewApp(name string) *App {
	return &App{name: requireNotBlank(name, "app name")}
}

func (a *App) WithEnvironmentID(environmentID string) *App {
	a.environmentID = strings.TrimSpace(environmentID)
	return a
}

func (a *App) WithMetadata(name string, value string) *App {
	if a.metadata == nil {
		a.metadata = map[string]string{}
	}

	a.metadata[requireNotBlank(name, "metadata name")] = value
	return a
}

func (a *App) Add(resource ResourceBuilder) ResourceBuilder {
	if resource == nil {
		panic("resource is required")
	}

	for _, existing := range a.resources {
		if strings.EqualFold(existing.ResourceID(), resource.ResourceID()) {
			panic(fmt.Sprintf("resource %q is already defined", resource.ResourceID()))
		}
	}

	resource.setApp(a)
	a.resources = append(a.resources, resource)
	return resource
}

func (a *App) AddNetwork(name string) *NetworkResource {
	resource := NewNetworkResource(name)
	a.Add(resource)
	return resource
}

func (a *App) AddConfigurationStore(name string) *ConfigurationStoreResource {
	resource := NewConfigurationStoreResource(name)
	a.Add(resource)
	return resource
}

func (a *App) AddSecretsVault(name string) *SecretsVaultResource {
	resource := NewSecretsVaultResource(name)
	a.Add(resource)
	return resource
}

func (a *App) AddLoadBalancer(name string) *LoadBalancerResource {
	resource := NewLoadBalancerResource(name)
	a.Add(resource)
	return resource
}

func (a *App) AddGoApp(name string, projectPath string) *GoAppResource {
	resource := NewGoAppResource(name, projectPath)
	a.Add(resource)
	return resource
}

func (a *App) DefaultNetwork() *NetworkResource {
	for _, resource := range a.resources {
		if strings.EqualFold(resource.ResourceID(), "network:host") {
			network, ok := resource.(*NetworkResource)
			if !ok {
				panic(fmt.Sprintf("resource %q is already defined as %q", resource.ResourceID(), resource.Type()))
			}

			return network
		}
	}

	return a.AddNetwork("host").
		WithResourceID("network:host").
		WithDisplayName("Host network").
		WithNetworkKind("Host").
		WithHostReadiness("hostReady")
}

func (a *App) BuildTemplate() ResourceTemplate {
	resources := make([]map[string]any, 0, len(a.resources))
	for _, resource := range a.resources {
		resources = append(resources, resource.build())
	}

	return ResourceTemplate{
		Name:          a.name,
		Resources:     resources,
		EnvironmentID: a.environmentID,
		Metadata:      a.metadata,
	}
}

func (a *App) ToJSON() (string, error) {
	data, err := json.MarshalIndent(a.BuildTemplate(), "", "  ")
	if err != nil {
		return "", err
	}

	return string(data) + "\n", nil
}

func (a *App) WriteTemplate(path string) (string, error) {
	if strings.TrimSpace(path) == "" {
		return "", errors.New("template path is required")
	}

	jsonValue, err := a.ToJSON()
	if err != nil {
		return "", err
	}

	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return "", err
	}

	if err := os.WriteFile(path, []byte(jsonValue), 0o644); err != nil {
		return "", err
	}

	return path, nil
}

func (a *App) Run(args []string) int {
	return a.RunWithOptions(args, DefaultLauncherOptions())
}

func (a *App) RunWithOptions(args []string, defaults LauncherOptions) int {
	command := "run"
	if len(args) > 0 && !strings.HasPrefix(args[0], "-") {
		command = args[0]
		args = args[1:]
	}

	options := OptionsFromArgsWithDefaults(args, defaults)
	switch command {
	case "template", "toJson", "json":
		jsonValue, err := a.ToJSON()
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			return 1
		}

		fmt.Print(jsonValue)
		return 0
	case "apply":
		result, err := a.Apply(options)
		return exitCode(result, err)
	case "start":
		result, err := a.Start(options)
		return exitCode(result, err)
	case "run":
		result, err := a.ForegroundRun(options)
		return exitCode(result, err)
	default:
		fmt.Fprintf(os.Stderr, "unknown command: %s\n", command)
		return 2
	}
}

func exitCode(result CommandResult, err error) int {
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	return result.ExitCode
}

func reference(resource ResourceHandle, relationship ResourceReferenceRelationship) ResourceReference {
	return ResourceReference{
		ResourceID:     resource.ResourceID(),
		Relationship:   relationship,
		AddressingMode: AddressingModeResourceID,
		TypeID:         resource.Type(),
		ProviderID:     resource.ProviderID(),
	}
}

func requireNotBlank(value string, name string) string {
	value = strings.TrimSpace(value)
	if value == "" {
		panic(name + " is required")
	}

	return value
}

func requireNotNil[T any](value T, name string) T {
	if any(value) == nil {
		panic(name + " is required")
	}

	return value
}
