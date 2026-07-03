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
	entries  []ConfigurationSeedEntry
}

type ConfigurationSeedEntry struct {
	Name  string `json:"name"`
	Value string `json:"value"`
}

type ConfigurationEntryReference struct {
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

func (r *ConfigurationStoreResource) WithSetting(name string, value string) *ConfigurationStoreResource {
	r.entries = append(r.entries, ConfigurationSeedEntry{
		Name:  requireNotBlank(name, "configuration entry name"),
		Value: value,
	})
	return r
}

func (r *ConfigurationStoreResource) Entry(name string) ConfigurationEntryReference {
	return ConfigurationEntryReference{
		StoreResourceID: r.ResourceID(),
		Name:            requireNotBlank(name, "configuration entry name"),
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

	if len(r.entries) > 0 {
		document["seed"] = map[string]any{
			"entries": r.entries,
		}
	}

	return document
}

type SecretsVaultResource struct {
	baseResource
	endpoint string
	secrets  []SecretSeedValue
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

func (r *SecretsVaultResource) WithSecret(name string, value string, version ...string) *SecretsVaultResource {
	secret := SecretSeedValue{
		Name:  requireNotBlank(name, "secret name"),
		Value: value,
	}
	if len(version) > 0 {
		secret.Version = version[0]
	}

	r.secrets = append(r.secrets, secret)
	return r
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

func (r *SecretsVaultResource) setApp(app *App) {
	r.baseResource.setApp(app)
}

func (r *SecretsVaultResource) build() map[string]any {
	document := r.commonDocument()
	if r.endpoint != "" {
		document["endpoint"] = r.endpoint
	}

	if len(r.secrets) > 0 {
		document["seed"] = map[string]any{
			"secrets": r.secrets,
		}
	}

	return document
}
