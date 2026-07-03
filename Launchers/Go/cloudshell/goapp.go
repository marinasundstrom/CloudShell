package cloudshell

type EnvironmentVariableValue struct {
	Value                 string                       `json:"value,omitempty"`
	ConfigurationEntryRef *ConfigurationEntryReference `json:"configurationEntryRef,omitempty"`
	SecretRef             *SecretReference             `json:"secretRef,omitempty"`
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

type GoAppResource struct {
	baseResource
	projectPath          string
	command              string
	packagePath          string
	binaryPath           string
	arguments            string
	serviceDiscoveryName string
	environment          map[string]EnvironmentVariableValue
	references           []ResourceReference
	endpoints            []EndpointRequest
	healthChecks         []HealthCheck
	consoleLogs          bool
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

func (r *GoAppResource) WithEnvironmentVariable(name string, value string) *GoAppResource {
	r.environment[requireNotBlank(name, "environment variable name")] = EnvironmentVariableValue{
		Value: value,
	}
	return r
}

func (r *GoAppResource) WithConfigurationEntry(name string, reference ConfigurationEntryReference) *GoAppResource {
	r.environment[requireNotBlank(name, "environment variable name")] = EnvironmentVariableValue{
		ConfigurationEntryRef: &reference,
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
	goAttributes := map[string]any{
		"command":     r.command,
		"packagePath": r.packagePath,
	}
	if r.binaryPath != "" {
		goAttributes["binaryPath"] = r.binaryPath
	}

	if r.arguments != "" {
		goAttributes["arguments"] = r.arguments
	}

	document["go"] = goAttributes

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
		project["endpointRequests"] = r.endpoints
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
