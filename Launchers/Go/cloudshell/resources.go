package cloudshell

import (
	"fmt"
	"strings"
)

type baseResource struct {
	app         *App
	name        string
	typeID      string
	providerID  string
	resourceID  string
	displayName string
	dependsOn   []ResourceReference
}

func newBaseResource(name string, typeID string, providerID string) baseResource {
	name = requireNotBlank(name, "resource name")
	typeID = requireNotBlank(typeID, "resource type")
	return baseResource{
		name:       name,
		typeID:     typeID,
		providerID: providerID,
		resourceID: typeID + ":" + name,
	}
}

func (r *baseResource) Name() string {
	return r.name
}

func (r *baseResource) Type() string {
	return r.typeID
}

func (r *baseResource) ProviderID() string {
	return r.providerID
}

func (r *baseResource) ResourceID() string {
	return r.resourceID
}

func (r *baseResource) setApp(app *App) {
	r.app = app
}

func (r *baseResource) withResourceID(resourceID string) {
	resourceID = requireNotBlank(resourceID, "resource id")
	if r.app != nil {
		for _, existing := range r.app.resources {
			if strings.EqualFold(existing.ResourceID(), resourceID) &&
				(!strings.EqualFold(existing.Name(), r.name) ||
					!strings.EqualFold(existing.Type(), r.typeID)) {
				panic(fmt.Sprintf("resource %q is already defined", resourceID))
			}
		}
	}

	r.resourceID = resourceID
}

func (r *baseResource) withDisplayName(displayName string) {
	r.displayName = displayName
}

func (r *baseResource) dependsOnResource(resource ResourceHandle) {
	r.dependsOn = append(r.dependsOn, reference(resource, RelationshipDependsOn))
}

func (r *baseResource) dependsOnResourceID(resourceID string) {
	r.dependsOn = append(r.dependsOn, ResourceReference{
		ResourceID:     requireNotBlank(resourceID, "resource id"),
		Relationship:   RelationshipDependsOn,
		AddressingMode: AddressingModeResourceID,
	})
}

func (r *baseResource) projectAsContainerApp() {
	previousDefaultResourceID := r.typeID + ":" + r.name
	r.typeID = "application.container-app"
	r.providerID = "applications.container-app"
	if r.resourceID == "" || strings.EqualFold(r.resourceID, previousDefaultResourceID) {
		r.resourceID = r.typeID + ":" + r.name
	}
}

func (r *baseResource) commonDocument() map[string]any {
	document := map[string]any{
		"name":       r.name,
		"type":       r.typeID,
		"resourceId": r.resourceID,
	}

	if r.providerID != "" {
		document["providerId"] = r.providerID
	}

	if r.displayName != "" {
		document["displayName"] = r.displayName
	}

	if len(r.dependsOn) > 0 {
		document["dependsOn"] = r.dependsOn
	}

	return document
}

type NetworkResource struct {
	baseResource
	kind          string
	hostReadiness string
}

func NewNetworkResource(name string) *NetworkResource {
	return &NetworkResource{
		baseResource: newBaseResource(name, "cloudshell.network", "cloudshell.network"),
	}
}

func (r *NetworkResource) WithResourceID(resourceID string) *NetworkResource {
	r.withResourceID(resourceID)
	return r
}

func (r *NetworkResource) WithDisplayName(displayName string) *NetworkResource {
	r.withDisplayName(displayName)
	return r
}

func (r *NetworkResource) DependsOn(resource ResourceHandle) *NetworkResource {
	r.dependsOnResource(resource)
	return r
}

func (r *NetworkResource) WithNetworkKind(kind string) *NetworkResource {
	r.kind = kind
	return r
}

func (r *NetworkResource) WithHostReadiness(hostReadiness string) *NetworkResource {
	r.hostReadiness = hostReadiness
	return r
}

func (r *NetworkResource) setApp(app *App) {
	r.baseResource.setApp(app)
}

func (r *NetworkResource) build() map[string]any {
	document := r.commonDocument()
	network := map[string]any{}
	if r.kind != "" {
		network["kind"] = r.kind
	}

	if r.hostReadiness != "" {
		network["hostReadiness"] = r.hostReadiness
	}

	if len(network) > 0 {
		document["network"] = network
	}

	return document
}

type ConfigurationStoreResource struct {
	baseResource
	endpoint string
	settings []ConfigurationSeedSetting
}

type ConfigurationStoreSeed struct {
	settings []ConfigurationSeedSetting
}

type ConfigurationSeedSetting struct {
	Name  string `json:"name"`
	Value string `json:"value"`
}

type ConfigurationSettingReference struct {
	StoreResourceID string `json:"storeResourceId"`
	Name            string `json:"name"`
	Version         string `json:"version,omitempty"`
}

func NewConfigurationStoreResource(name string) *ConfigurationStoreResource {
	return &ConfigurationStoreResource{
		baseResource: newBaseResource(name, "configuration.store", "configuration"),
	}
}

func (r *ConfigurationStoreResource) WithResourceID(resourceID string) *ConfigurationStoreResource {
	r.withResourceID(resourceID)
	return r
}

func (r *ConfigurationStoreResource) WithDisplayName(displayName string) *ConfigurationStoreResource {
	r.withDisplayName(displayName)
	return r
}

func (r *ConfigurationStoreResource) WithEndpoint(endpoint string) *ConfigurationStoreResource {
	r.endpoint = endpoint
	return r
}

func (r *ConfigurationStoreResource) WithSeed(configure func(seed *ConfigurationStoreSeed)) *ConfigurationStoreResource {
	requireNotNil(configure, "configuration store seed configure")

	seed := &ConfigurationStoreSeed{}
	configure(seed)
	r.settings = append([]ConfigurationSeedSetting{}, seed.settings...)
	return r
}

func (s *ConfigurationStoreSeed) Setting(name string, value string) *ConfigurationStoreSeed {
	s.settings = append(s.settings, ConfigurationSeedSetting{
		Name:  requireNotBlank(name, "configuration setting name"),
		Value: value,
	})
	return s
}

func (r *ConfigurationStoreResource) Setting(name string) ConfigurationSettingReference {
	return ConfigurationSettingReference{
		StoreResourceID: r.ResourceID(),
		Name:            requireNotBlank(name, "configuration setting name"),
	}
}

func (r *ConfigurationStoreResource) setApp(app *App) {
	r.baseResource.setApp(app)
}

func (r *ConfigurationStoreResource) build() map[string]any {
	document := r.commonDocument()
	if r.endpoint != "" {
		document["endpoint"] = r.endpoint
	}

	if len(r.settings) > 0 {
		document["seed"] = map[string]any{
			"settings": r.settings,
		}
	}

	return document
}

type SecretsVaultResource struct {
	baseResource
	endpoint     string
	secrets      []SecretSeedValue
	certificates []CertificateSeedValue
}

type SecretsVaultSeed struct {
	secrets      []SecretSeedValue
	certificates []CertificateSeedValue
}

type SecretSeedValue struct {
	Name    string `json:"name"`
	Value   string `json:"value"`
	Version string `json:"version,omitempty"`
}

type SecretReference struct {
	VaultResourceID string `json:"vaultResourceId"`
	Name            string `json:"name"`
	Version         string `json:"version,omitempty"`
}

type CertificateSeedValue struct {
	Name        string `json:"name"`
	Value       string `json:"value"`
	Version     string `json:"version,omitempty"`
	ContentType string `json:"contentType,omitempty"`
}

type CertificateReference struct {
	VaultResourceID string `json:"vaultResourceId"`
	Name            string `json:"name"`
	Version         string `json:"version,omitempty"`
}

func NewSecretsVaultResource(name string) *SecretsVaultResource {
	return &SecretsVaultResource{
		baseResource: newBaseResource(name, "secrets.vault", "secrets-vault"),
	}
}

func (r *SecretsVaultResource) WithResourceID(resourceID string) *SecretsVaultResource {
	r.withResourceID(resourceID)
	return r
}

func (r *SecretsVaultResource) WithDisplayName(displayName string) *SecretsVaultResource {
	r.withDisplayName(displayName)
	return r
}

func (r *SecretsVaultResource) WithEndpoint(endpoint string) *SecretsVaultResource {
	r.endpoint = endpoint
	return r
}

func (r *SecretsVaultResource) WithSeed(configure func(seed *SecretsVaultSeed)) *SecretsVaultResource {
	requireNotNil(configure, "secrets vault seed configure")

	seed := &SecretsVaultSeed{}
	configure(seed)
	r.secrets = append([]SecretSeedValue{}, seed.secrets...)
	r.certificates = append([]CertificateSeedValue{}, seed.certificates...)
	return r
}

func (s *SecretsVaultSeed) Secret(name string, value string, version ...string) *SecretsVaultSeed {
	secret := SecretSeedValue{
		Name:  requireNotBlank(name, "secret name"),
		Value: value,
	}
	if len(version) > 0 {
		secret.Version = version[0]
	}

	s.secrets = append(s.secrets, secret)
	return s
}

func (s *SecretsVaultSeed) Certificate(name string, value string, contentType string, version ...string) *SecretsVaultSeed {
	certificate := CertificateSeedValue{
		Name:        requireNotBlank(name, "certificate name"),
		Value:       value,
		ContentType: contentType,
	}
	if len(version) > 0 {
		certificate.Version = version[0]
	}

	s.certificates = append(s.certificates, certificate)
	return s
}

func (r *SecretsVaultResource) Secret(name string, version ...string) SecretReference {
	reference := SecretReference{
		VaultResourceID: r.ResourceID(),
		Name:            requireNotBlank(name, "secret name"),
	}
	if len(version) > 0 {
		reference.Version = version[0]
	}

	return reference
}

func (r *SecretsVaultResource) Certificate(name string, version ...string) CertificateReference {
	reference := CertificateReference{
		VaultResourceID: r.ResourceID(),
		Name:            requireNotBlank(name, "certificate name"),
	}
	if len(version) > 0 {
		reference.Version = version[0]
	}

	return reference
}

func (r *SecretsVaultResource) setApp(app *App) {
	r.baseResource.setApp(app)
}

func (r *SecretsVaultResource) build() map[string]any {
	document := r.commonDocument()
	if r.endpoint != "" {
		document["endpoint"] = r.endpoint
	}

	if len(r.secrets) > 0 || len(r.certificates) > 0 {
		seed := map[string]any{}
		if len(r.secrets) > 0 {
			seed["secrets"] = r.secrets
		}
		if len(r.certificates) > 0 {
			seed["certificates"] = r.certificates
		}
		document["seed"] = seed
	}

	return document
}

type LoadBalancerResource struct {
	baseResource
	provider     string
	hostResource string
	entrypoints  []LoadBalancerEntrypointValue
	routes       []LoadBalancerRouteValue
}

type LoadBalancerEntrypointValue struct {
	Name           string                `json:"name"`
	Protocol       string                `json:"protocol"`
	Port           int                   `json:"port"`
	Exposure       string                `json:"exposure"`
	CertificateRef *CertificateReference `json:"certificateRef,omitempty"`
}

type LoadBalancerRouteValue struct {
	ID             string                       `json:"id"`
	Name           string                       `json:"name"`
	Kind           string                       `json:"kind"`
	EntrypointName string                       `json:"entrypointName"`
	Match          LoadBalancerRouteMatchValue  `json:"match"`
	Target         LoadBalancerRouteTargetValue `json:"target"`
}

type LoadBalancerRouteMatchValue struct {
	Host       string `json:"host,omitempty"`
	PathPrefix string `json:"pathPrefix,omitempty"`
	Port       int    `json:"port,omitempty"`
}

type LoadBalancerRouteTargetValue struct {
	Resource     ResourceReference `json:"resource"`
	EndpointName string            `json:"endpointName,omitempty"`
	Port         int               `json:"port,omitempty"`
}

func NewLoadBalancerResource(name string) *LoadBalancerResource {
	return &LoadBalancerResource{
		baseResource: newBaseResource(name, "cloudshell.loadBalancer", "cloudshell.load-balancer"),
	}
}

func (r *LoadBalancerResource) WithResourceID(resourceID string) *LoadBalancerResource {
	r.withResourceID(resourceID)
	return r
}

func (r *LoadBalancerResource) WithDisplayName(displayName string) *LoadBalancerResource {
	r.withDisplayName(displayName)
	return r
}

func (r *LoadBalancerResource) WithProvider(provider string) *LoadBalancerResource {
	r.provider = requireNotBlank(provider, "load balancer provider")
	return r
}

func (r *LoadBalancerResource) UseHost(host ResourceHandle) *LoadBalancerResource {
	if host == nil {
		panic("load balancer host resource is required")
	}

	r.hostResource = host.ResourceID()
	r.dependsOnResource(host)
	return r
}

func (r *LoadBalancerResource) ExposeHTTP(port ...int) *LoadBalancerResource {
	return r.addEntrypoint("http", "Http", firstInt(port, 80), "Public", nil)
}

func (r *LoadBalancerResource) ExposeHTTPS(certificate CertificateReference, port ...int) *LoadBalancerResource {
	return r.addEntrypoint("https", "Https", firstInt(port, 443), "Public", &certificate)
}

func (r *LoadBalancerResource) ExposeTCP(port int) *LoadBalancerResource {
	name := fmt.Sprintf("tcp-%d", port)
	return r.addEntrypoint(name, "Tcp", port, "Public", nil)
}

func (r *LoadBalancerResource) MapHost(host string, target ResourceHandle, port int, entrypoint ...string) *LoadBalancerResource {
	return r.addRoute(
		"Http",
		"",
		fmt.Sprintf("%s to %s:%d", host, target.ResourceID(), port),
		firstString(entrypoint, "http"),
		LoadBalancerRouteMatchValue{Host: requireNotBlank(host, "route host")},
		r.createTarget(target, "", port),
		target)
}

func (r *LoadBalancerResource) MapPath(host string, pathPrefix string, target ResourceHandle, port int, entrypoint ...string) *LoadBalancerResource {
	return r.addRoute(
		"Http",
		"",
		fmt.Sprintf("%s%s to %s:%d", host, pathPrefix, target.ResourceID(), port),
		firstString(entrypoint, "http"),
		LoadBalancerRouteMatchValue{
			Host:       requireNotBlank(host, "route host"),
			PathPrefix: requireNotBlank(pathPrefix, "route path prefix"),
		},
		r.createTarget(target, "", port),
		target)
}

func (r *LoadBalancerResource) MapTCP(port int, target ResourceHandle, targetPort int) *LoadBalancerResource {
	return r.addRoute(
		"Tcp",
		"",
		fmt.Sprintf("tcp %d to %s:%d", port, target.ResourceID(), targetPort),
		fmt.Sprintf("tcp-%d", port),
		LoadBalancerRouteMatchValue{Port: port},
		r.createTarget(target, "", targetPort),
		target)
}

func (r *LoadBalancerResource) addEntrypoint(
	name string,
	protocol string,
	port int,
	exposure string,
	certificate *CertificateReference) *LoadBalancerResource {
	name = requireNotBlank(name, "load balancer entrypoint name")
	for index, entrypoint := range r.entrypoints {
		if strings.EqualFold(entrypoint.Name, name) {
			r.entrypoints = append(r.entrypoints[:index], r.entrypoints[index+1:]...)
			break
		}
	}

	r.entrypoints = append(r.entrypoints, LoadBalancerEntrypointValue{
		Name:           name,
		Protocol:       protocol,
		Port:           port,
		Exposure:       exposure,
		CertificateRef: certificate,
	})
	return r
}

func (r *LoadBalancerResource) addRoute(
	kind string,
	id string,
	name string,
	entrypoint string,
	match LoadBalancerRouteMatchValue,
	target LoadBalancerRouteTargetValue,
	targetResource ResourceHandle) *LoadBalancerResource {
	if id == "" {
		id = r.createRouteID(kind, match, targetResource, target)
	}

	for index, route := range r.routes {
		if strings.EqualFold(route.ID, id) {
			r.routes = append(r.routes[:index], r.routes[index+1:]...)
			break
		}
	}

	r.routes = append(r.routes, LoadBalancerRouteValue{
		ID:             id,
		Name:           name,
		Kind:           kind,
		EntrypointName: requireNotBlank(entrypoint, "route entrypoint"),
		Match:          match,
		Target:         target,
	})
	r.dependsOnResource(targetResource)
	return r
}

func (r *LoadBalancerResource) createTarget(
	target ResourceHandle,
	endpointName string,
	port int) LoadBalancerRouteTargetValue {
	if target == nil {
		panic("load balancer target resource is required")
	}

	return LoadBalancerRouteTargetValue{
		Resource:     reference(target, RelationshipReference),
		EndpointName: strings.TrimSpace(endpointName),
		Port:         port,
	}
}

func (r *LoadBalancerResource) createRouteID(
	kind string,
	match LoadBalancerRouteMatchValue,
	targetResource ResourceHandle,
	target LoadBalancerRouteTargetValue) string {
	source := strings.Trim(strings.ReplaceAll(match.Host+"-"+match.PathPrefix, "/", "-"), "-")
	if strings.EqualFold(kind, "Tcp") {
		source = fmt.Sprintf("tcp-%d", match.Port)
	}
	if source == "" {
		source = "route"
	}

	targetPart := target.EndpointName
	if targetPart == "" && target.Port > 0 {
		targetPart = fmt.Sprintf("%d", target.Port)
	}
	if targetPart == "" {
		targetPart = "target"
	}

	return fmt.Sprintf("%s:route:%s:%s:%s", r.ResourceID(), source, targetResource.ResourceID(), targetPart)
}

func (r *LoadBalancerResource) setApp(app *App) {
	r.baseResource.setApp(app)
}

func (r *LoadBalancerResource) build() map[string]any {
	document := r.commonDocument()
	loadBalancer := map[string]any{}
	if r.provider != "" {
		loadBalancer["provider"] = r.provider
	}
	if r.hostResource != "" {
		loadBalancer["hostResourceId"] = r.hostResource
	}
	if len(r.entrypoints) > 0 {
		loadBalancer["entrypointDefinitions"] = r.entrypoints
	}
	if len(r.routes) > 0 {
		loadBalancer["routeDefinitions"] = r.routes
	}
	if len(loadBalancer) > 0 {
		document["loadBalancer"] = loadBalancer
	}

	return document
}

func firstInt(values []int, fallback int) int {
	if len(values) == 0 {
		return fallback
	}

	return values[0]
}

func firstString(values []string, fallback string) string {
	if len(values) == 0 || strings.TrimSpace(values[0]) == "" {
		return fallback
	}

	return strings.TrimSpace(values[0])
}
