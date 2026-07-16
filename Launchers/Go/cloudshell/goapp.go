package cloudshell

import "strings"

type EnvironmentVariableValue struct {
	Value                   string                         `json:"value,omitempty"`
	ConfigurationSettingRef *ConfigurationSettingReference `json:"configurationSettingRef,omitempty"`
	SecretRef               *SecretReference               `json:"secretRef,omitempty"`
}

type EndpointRequest struct {
	Name       string             `json:"name"`
	Protocol   string             `json:"protocol"`
	TargetPort int                `json:"targetPort,omitempty"`
	Host       string             `json:"host,omitempty"`
	Port       int                `json:"port,omitempty"`
	Exposure   string             `json:"exposure,omitempty"`
	Network    *ResourceReference `json:"network,omitempty"`
}

type HealthCheck struct {
	Name   string      `json:"name"`
	Type   string      `json:"type"`
	Source ProbeSource `json:"source"`
}

type ProbeSource struct {
	Kind string    `json:"kind"`
	HTTP HTTPProbe `json:"http"`
}

type HTTPProbe struct {
	Path         string `json:"path"`
	EndpointName string `json:"endpointName,omitempty"`
}

type ContainerAppOptions struct {
	Image        string
	Registry     string
	Tag          string
	BuildContext string
	Dockerfile   string
	Replicas     int
}

type GoAppResource struct {
	baseResource
	projectPath           string
	command               string
	packagePath           string
	binaryPath            string
	arguments             string
	serviceDiscoveryName  string
	environment           map[string]EnvironmentVariableValue
	references            []ResourceReference
	endpoints             []EndpointRequest
	healthChecks          []HealthCheck
	containerApp          bool
	containerImage        string
	containerRegistry     string
	containerBuildContext string
	containerDockerfile   string
	containerReplicas     int
	consoleLogs           bool
}

func NewGoAppResource(name string, projectPath string) *GoAppResource {
	return &GoAppResource{
		baseResource: newBaseResource(name, "application.go-app", "applications.go-app"),
		projectPath:  requireNotBlank(projectPath, "Go app project path"),
		command:      "go",
		packagePath:  ".",
		environment:  map[string]EnvironmentVariableValue{},
	}
}

func (r *GoAppResource) WithResourceID(resourceID string) *GoAppResource {
	r.withResourceID(resourceID)
	return r
}

func (r *GoAppResource) WithDisplayName(displayName string) *GoAppResource {
	r.withDisplayName(displayName)
	return r
}

func (r *GoAppResource) DependsOn(resource ResourceHandle) *GoAppResource {
	r.dependsOnResource(resource)
	return r
}

func (r *GoAppResource) DependsOnResourceID(resourceID string) *GoAppResource {
	r.dependsOnResourceID(resourceID)
	return r
}

func (r *GoAppResource) WithCommand(command string) *GoAppResource {
	r.command = requireNotBlank(command, "Go command")
	return r
}

func (r *GoAppResource) WithPackagePath(packagePath string) *GoAppResource {
	r.packagePath = requireNotBlank(packagePath, "Go package path")
	return r
}

func (r *GoAppResource) WithBinaryPath(binaryPath string) *GoAppResource {
	r.binaryPath = requireNotBlank(binaryPath, "Go binary path")
	return r
}

func (r *GoAppResource) WithArguments(arguments string) *GoAppResource {
	r.arguments = arguments
	return r
}

func (r *GoAppResource) WithServiceDiscovery() *GoAppResource {
	r.serviceDiscoveryName = r.Name()
	return r
}

func (r *GoAppResource) WithServiceDiscoveryName(name string) *GoAppResource {
	r.serviceDiscoveryName = requireNotBlank(name, "service discovery name")
	return r
}

func (r *GoAppResource) WithIdentity(providerID string, options ResourceIdentityOptions) *GoAppResource {
	r.withIdentity(providerID, options)
	return r
}

func (r *GoAppResource) RequireIdentity(name string) *GoAppResource {
	r.requireIdentity(name)
	return r
}

func (r *GoAppResource) ProvisionIdentityOnStartup(enabled ...bool) *GoAppResource {
	provision := true
	if len(enabled) > 0 {
		provision = enabled[0]
	}

	r.provisionIdentityOnStartup(provision)
	return r
}

func (r *GoAppResource) AsContainerApp(options ContainerAppOptions) *GoAppResource {
	r.projectAsContainerApp()
	r.containerApp = true
	if options.Image != "" {
		r.containerImage = options.Image
	} else {
		r.containerImage = defaultContainerImage("go", r.Name(), options.Tag)
	}

	r.containerRegistry = options.Registry
	if options.BuildContext != "" {
		r.containerBuildContext = options.BuildContext
	} else {
		r.containerBuildContext = r.projectPath
	}

	r.containerDockerfile = options.Dockerfile
	if options.Replicas > 0 {
		r.containerReplicas = options.Replicas
	} else {
		r.containerReplicas = 1
	}

	return r
}

func (r *GoAppResource) WithEnvironmentVariable(name string, value string) *GoAppResource {
	r.environment[requireNotBlank(name, "environment variable name")] = EnvironmentVariableValue{
		Value: value,
	}
	return r
}

func (r *GoAppResource) WithConfigurationSetting(name string, reference ConfigurationSettingReference) *GoAppResource {
	r.environment[requireNotBlank(name, "environment variable name")] = EnvironmentVariableValue{
		ConfigurationSettingRef: &reference,
	}
	return r
}

func (r *GoAppResource) WithSecret(name string, reference SecretReference) *GoAppResource {
	r.environment[requireNotBlank(name, "environment variable name")] = EnvironmentVariableValue{
		SecretRef: &reference,
	}
	return r
}

func (r *GoAppResource) WithReference(resource ResourceHandle) *GoAppResource {
	r.references = append(r.references, reference(resource, RelationshipReference))
	return r
}

func (r *GoAppResource) WithHttpEndpoint(host string, port int, targetPort int, network ResourceHandle) *GoAppResource {
	endpoint := EndpointRequest{
		Name:       "http",
		Protocol:   "http",
		TargetPort: targetPort,
		Host:       host,
		Port:       port,
		Exposure:   "Local",
	}
	if network != nil {
		networkReference := reference(network, RelationshipReference)
		endpoint.Network = &networkReference
	}

	r.endpoints = append(r.endpoints, endpoint)
	return r
}

func (r *GoAppResource) WithHttpHealthCheck(path string) *GoAppResource {
	r.healthChecks = append(r.healthChecks, httpCheck("health", "health", path))
	return r
}

func (r *GoAppResource) WithHttpLivenessCheck(path string) *GoAppResource {
	r.healthChecks = append(r.healthChecks, httpCheck("alive", "liveness", path))
	return r
}

func (r *GoAppResource) WithDefaultConsoleLogSource() *GoAppResource {
	r.consoleLogs = true
	return r
}

func (r *GoAppResource) setApp(app *App) {
	r.baseResource.setApp(app)
}

func (r *GoAppResource) build() map[string]any {
	document := r.commonDocument()
	document["command"] = r.command
	document["packagePath"] = r.packagePath
	if r.binaryPath != "" {
		document["binaryPath"] = r.binaryPath
	}

	if r.arguments != "" {
		document["arguments"] = r.arguments
	}

	project := map[string]any{
		"path": r.projectPath,
	}
	if r.serviceDiscoveryName != "" {
		project["serviceDiscoveryName"] = r.serviceDiscoveryName
	}

	if len(r.environment) > 0 {
		project["environmentVariables"] = r.environment
	}

	if len(r.references) > 0 {
		project["references"] = r.references
	}

	if len(r.endpoints) > 0 {
		if r.containerApp {
			document["container"] = r.containerDocument()
		} else {
			project["endpointRequests"] = r.endpoints
		}
	} else if r.containerApp {
		document["container"] = r.containerDocument()
	}

	document["project"] = project
	if len(r.healthChecks) > 0 {
		document["health"] = map[string]any{
			"checks": r.healthChecks,
		}
	}

	if r.consoleLogs {
		document["logs"] = map[string]any{
			"sources": []map[string]any{
				{
					"id":           "console",
					"name":         "Console logs",
					"kind":         "processOutput",
					"format":       "plainText",
					"capabilities": []string{"read", "stream"},
					"description":  "Provider-captured process console output.",
					"origin":       "providerDefault",
					"purpose":      "default",
					"availability": "resourceRunning",
				},
			},
		}
	}

	return document
}

func (r *GoAppResource) containerDocument() map[string]any {
	container := map[string]any{
		"image":    r.containerImage,
		"replicas": r.containerReplicas,
	}
	if r.containerRegistry != "" {
		container["registry"] = r.containerRegistry
	}

	if r.containerBuildContext != "" {
		container["buildContext"] = r.containerBuildContext
	}

	if r.containerDockerfile != "" {
		container["dockerfile"] = r.containerDockerfile
	}

	if len(r.endpoints) > 0 {
		container["endpointRequests"] = r.endpoints
	}

	return container
}

func defaultContainerImage(language string, name string, tag string) string {
	normalized := strings.Trim(strings.Map(func(character rune) rune {
		if character >= 'a' && character <= 'z' || character >= '0' && character <= '9' {
			return character
		}

		if character >= 'A' && character <= 'Z' {
			return character + ('a' - 'A')
		}

		return '-'
	}, name), "-")
	if normalized == "" {
		normalized = "app"
	}

	if tag == "" {
		tag = "dev"
	}

	return "cloudshell-" + language + "-" + normalized + ":" + tag
}

func httpCheck(name string, checkType string, path string) HealthCheck {
	return HealthCheck{
		Name: name,
		Type: checkType,
		Source: ProbeSource{
			Kind: "http",
			HTTP: HTTPProbe{
				Path:         requireNotBlank(path, "health check path"),
				EndpointName: "http",
			},
		},
	}
}
