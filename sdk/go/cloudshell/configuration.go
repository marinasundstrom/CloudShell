package cloudshell

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/url"
	"os"
	"sort"
	"strings"
	"unicode"
)

type ConfigurationSetting struct {
	Name  string `json:"name"`
	Value string `json:"value"`
}

type ConfigurationStoreClient struct {
	SettingsEndpoint string
	Credential       TokenCredential
	Scopes           []string
	HTTPClient       *http.Client
}

func NewConfigurationStoreClient(settingsEndpoint string, credential TokenCredential) *ConfigurationStoreClient {
	if credential == nil {
		credential = NewDefaultCredential()
	}

	return &ConfigurationStoreClient{
		SettingsEndpoint: settingsEndpoint,
		Credential:       credential,
		Scopes:           []string{DefaultScope},
	}
}

func ConfigurationStoreFromEnvironment(serviceName string, credential TokenCredential) (*ConfigurationStoreClient, error) {
	endpoint, ok := findEndpoint("CLOUDSHELL_CONFIGURATION_", serviceName, nil)
	if !ok {
		return nil, errors.New("no CloudShell configuration store endpoint was found in the environment")
	}

	return NewConfigurationStoreClient(endpoint, credential), nil
}

func (c *ConfigurationStoreClient) GetSettings(ctx context.Context) ([]ConfigurationSetting, error) {
	var settings []ConfigurationSetting
	if err := c.send(ctx, c.SettingsEndpoint, &settings); err != nil {
		return nil, err
	}

	return settings, nil
}

func (c *ConfigurationStoreClient) GetSetting(ctx context.Context, name string) (*ConfigurationSetting, error) {
	if strings.TrimSpace(name) == "" {
		return nil, errors.New("configuration setting name is required")
	}

	endpoint, err := c.buildSettingEndpoint(name)
	if err != nil {
		return nil, err
	}

	var setting ConfigurationSetting
	if err := c.send(ctx, endpoint, &setting); err != nil {
		if errors.Is(err, ErrNotFound) {
			return nil, nil
		}

		return nil, err
	}

	return &setting, nil
}

func (c *ConfigurationStoreClient) ToMap(ctx context.Context, mapPortableHierarchySeparator bool) (map[string]string, error) {
	settings, err := c.GetSettings(ctx)
	if err != nil {
		return nil, err
	}

	result := map[string]string{}
	for _, setting := range settings {
		name := setting.Name
		if mapPortableHierarchySeparator {
			name = strings.ReplaceAll(name, "--", ":")
		}
		result[name] = setting.Value
	}

	return result, nil
}

func (c *ConfigurationStoreClient) send(ctx context.Context, endpoint string, target any) error {
	token, err := c.Credential.GetToken(ctx, c.Scopes)
	if err != nil {
		return err
	}

	request, err := http.NewRequestWithContext(ctx, http.MethodGet, endpoint, nil)
	if err != nil {
		return err
	}
	request.Header.Set("authorization", "Bearer "+token.Token)

	client := c.HTTPClient
	if client == nil {
		client = http.DefaultClient
	}

	response, err := client.Do(request)
	if err != nil {
		return err
	}
	defer response.Body.Close()

	if response.StatusCode == http.StatusNotFound {
		return ErrNotFound
	}
	if response.StatusCode < 200 || response.StatusCode > 299 {
		return errors.New("cloudshell configuration store returned " + response.Status)
	}

	return json.NewDecoder(response.Body).Decode(target)
}

func (c *ConfigurationStoreClient) buildSettingEndpoint(name string) (string, error) {
	endpoint, err := url.Parse(c.SettingsEndpoint)
	if err != nil {
		return "", err
	}

	endpoint.Path = strings.TrimRight(endpoint.Path, "/") + "/" + url.PathEscape(name)
	return endpoint.String(), nil
}

var ErrNotFound = errors.New("cloudshell resource not found")

func findEndpoint(prefix string, serviceName string, environment map[string]string) (string, bool) {
	normalizedServiceName := ""
	if strings.TrimSpace(serviceName) != "" {
		normalizedServiceName = normalizeEnvironmentSegment(serviceName)
	}

	values := environment
	if values == nil {
		values = map[string]string{}
		for _, entry := range os.Environ() {
			name, value, ok := strings.Cut(entry, "=")
			if ok {
				values[name] = value
			}
		}
	}

	keys := make([]string, 0, len(values))
	for key, value := range values {
		upperKey := strings.ToUpper(key)
		if strings.TrimSpace(value) == "" ||
			!strings.HasPrefix(upperKey, prefix) ||
			!strings.HasSuffix(upperKey, "_ENDPOINT") {
			continue
		}
		if normalizedServiceName != "" && !strings.Contains(upperKey, prefix+normalizedServiceName+"_") {
			continue
		}

		if _, err := url.ParseRequestURI(value); err == nil {
			keys = append(keys, key)
		}
	}

	sort.Strings(keys)
	if len(keys) == 0 {
		return "", false
	}

	return values[keys[0]], true
}

func normalizeEnvironmentSegment(value string) string {
	var builder strings.Builder
	for _, r := range strings.TrimSpace(value) {
		if (r >= 'a' && r <= 'z') || (r >= 'A' && r <= 'Z') || (r >= '0' && r <= '9') {
			builder.WriteRune(unicode.ToUpper(r))
		} else {
			builder.WriteRune('_')
		}
	}

	return strings.Trim(builder.String(), "_")
}
