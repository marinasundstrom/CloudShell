package cloudshell

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/url"
	"strings"
)

type SecretProperties struct {
	Name    string `json:"name"`
	Version string `json:"version,omitempty"`
}

type SecretValue struct {
	Name    string `json:"name"`
	Value   string `json:"value"`
	Version string `json:"version,omitempty"`
}

type SecretsVaultClient struct {
	SecretsEndpoint string
	Credential      TokenCredential
	Scopes          []string
	HTTPClient      *http.Client
}

func NewSecretsVaultClient(secretsEndpoint string, credential TokenCredential) *SecretsVaultClient {
	if credential == nil {
		credential = NewDefaultCredential()
	}

	return &SecretsVaultClient{
		SecretsEndpoint: strings.TrimRight(secretsEndpoint, "/"),
		Credential:      credential,
		Scopes:          []string{DefaultScope},
	}
}

func SecretsVaultFromEnvironment(vaultName string, credential TokenCredential) (*SecretsVaultClient, error) {
	endpoint, ok := findEndpoint("CLOUDSHELL_SECRETS_", vaultName, nil)
	if !ok {
		return nil, errors.New("no CloudShell Secrets Vault endpoint was found in the environment")
	}

	return NewSecretsVaultClient(endpoint, credential), nil
}

func (c *SecretsVaultClient) GetSecrets(ctx context.Context) ([]SecretProperties, error) {
	var secrets []SecretProperties
	if err := c.send(ctx, c.SecretsEndpoint, &secrets); err != nil {
		return nil, err
	}

	return secrets, nil
}

func (c *SecretsVaultClient) GetSecret(ctx context.Context, name string, version ...string) (*SecretValue, error) {
	if strings.TrimSpace(name) == "" {
		return nil, errors.New("secret name is required")
	}

	endpoint, err := c.buildSecretEndpoint(name, version...)
	if err != nil {
		return nil, err
	}

	var secret SecretValue
	if err := c.send(ctx, endpoint, &secret); err != nil {
		if errors.Is(err, ErrNotFound) {
			return nil, nil
		}

		return nil, err
	}

	return &secret, nil
}

func (c *SecretsVaultClient) send(ctx context.Context, endpoint string, target any) error {
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
		return errors.New("cloudshell Secrets Vault returned " + response.Status)
	}

	return json.NewDecoder(response.Body).Decode(target)
}

func (c *SecretsVaultClient) buildSecretEndpoint(name string, version ...string) (string, error) {
	endpoint, err := url.Parse(c.SecretsEndpoint)
	if err != nil {
		return "", err
	}

	endpoint.Path = strings.TrimRight(endpoint.Path, "/") + "/" + url.PathEscape(name)
	if len(version) > 0 && strings.TrimSpace(version[0]) != "" {
		query := endpoint.Query()
		query.Set("version", strings.TrimSpace(version[0]))
		endpoint.RawQuery = query.Encode()
	}

	return endpoint.String(), nil
}
