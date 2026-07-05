package cloudshell

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
)

const (
	DefaultScope = "ControlPlane.Access"

	IdentityTokenEndpointEnv = "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT"
	IdentityClientIDEnv      = "CLOUDSHELL_IDENTITY_CLIENT_ID"
	IdentityClientSecretEnv  = "CLOUDSHELL_IDENTITY_CLIENT_SECRET"
	IdentityScopeEnv         = "CLOUDSHELL_IDENTITY_SCOPE"

	ConfigDirectoryEnv = "CLOUDSHELL_CONFIG_DIR"
	ProfileEnv         = "CLOUDSHELL_PROFILE"
)

var ErrCredentialUnavailable = errors.New("cloudshell credential unavailable")

type AccessToken struct {
	Token     string
	ExpiresOn time.Time
}

type TokenCredential interface {
	GetToken(ctx context.Context, scopes []string) (AccessToken, error)
}

type DefaultCredential struct {
	Credentials []TokenCredential
}

func NewDefaultCredential() *DefaultCredential {
	return &DefaultCredential{
		Credentials: []TokenCredential{
			NewIdentityCredential(nil),
			NewEnvironmentTokenCredential(nil),
			NewProfileCredential(ProfileCredentialOptions{}),
		},
	}
}

func (c *DefaultCredential) GetToken(ctx context.Context, scopes []string) (AccessToken, error) {
	credentials := c.Credentials
	if len(credentials) == 0 {
		credentials = NewDefaultCredential().Credentials
	}

	var unavailable []string
	for _, credential := range credentials {
		token, err := credential.GetToken(ctx, scopes)
		if err == nil && strings.TrimSpace(token.Token) != "" {
			token.Token = strings.TrimSpace(token.Token)
			return token, nil
		}

		if errors.Is(err, ErrCredentialUnavailable) || strings.TrimSpace(token.Token) == "" {
			if err != nil {
				unavailable = append(unavailable, err.Error())
			}
			continue
		}

		return AccessToken{}, err
	}

	if len(unavailable) == 0 {
		return AccessToken{}, ErrCredentialUnavailable
	}

	return AccessToken{}, errors.Join(append([]error{ErrCredentialUnavailable}, stringsToErrors(unavailable)...)...)
}

type IdentityCredential struct {
	TokenEndpoint string
	ClientID      string
	ClientSecret  string
	Scope         string
	Environment   map[string]string
	HTTPClient    *http.Client

	mu     sync.Mutex
	cached AccessToken
}

func NewIdentityCredential(options *IdentityCredential) *IdentityCredential {
	if options == nil {
		return &IdentityCredential{}
	}

	return options
}

func (c *IdentityCredential) GetToken(ctx context.Context, scopes []string) (AccessToken, error) {
	c.mu.Lock()
	defer c.mu.Unlock()

	if c.cached.Token != "" && !c.cached.ExpiresOn.IsZero() && c.cached.ExpiresOn.After(time.Now().Add(time.Minute)) {
		return c.cached, nil
	}

	tokenEndpoint := firstNonEmpty(c.TokenEndpoint, envValue(c.Environment, IdentityTokenEndpointEnv))
	clientID := firstNonEmpty(c.ClientID, envValue(c.Environment, IdentityClientIDEnv))
	clientSecret := firstNonEmpty(c.ClientSecret, envValue(c.Environment, IdentityClientSecretEnv))
	if tokenEndpoint == "" || clientID == "" || clientSecret == "" {
		return AccessToken{}, ErrCredentialUnavailable
	}

	form := url.Values{}
	form.Set("grant_type", "client_credentials")
	form.Set("client_id", clientID)
	form.Set("client_secret", clientSecret)
	form.Set("scope", c.resolveScope(scopes))

	request, err := http.NewRequestWithContext(ctx, http.MethodPost, tokenEndpoint, strings.NewReader(form.Encode()))
	if err != nil {
		return AccessToken{}, err
	}
	request.Header.Set("content-type", "application/x-www-form-urlencoded")

	client := c.HTTPClient
	if client == nil {
		client = http.DefaultClient
	}

	response, err := client.Do(request)
	if err != nil {
		return AccessToken{}, err
	}
	defer response.Body.Close()

	body, err := io.ReadAll(response.Body)
	if err != nil {
		return AccessToken{}, err
	}
	if response.StatusCode < 200 || response.StatusCode > 299 {
		return AccessToken{}, errors.New("cloudshell identity token endpoint returned " + response.Status)
	}

	var tokenResponse identityTokenResponse
	if err := json.Unmarshal(body, &tokenResponse); err != nil {
		return AccessToken{}, err
	}
	if strings.TrimSpace(tokenResponse.AccessToken) == "" {
		return AccessToken{}, errors.New("cloudshell identity token endpoint returned no access token")
	}

	token := AccessToken{Token: strings.TrimSpace(tokenResponse.AccessToken)}
	if tokenResponse.ExpiresIn > 0 {
		token.ExpiresOn = time.Now().Add(time.Duration(tokenResponse.ExpiresIn) * time.Second)
	}

	c.cached = token
	return token, nil
}

func (c *IdentityCredential) resolveScope(scopes []string) string {
	if len(scopes) > 0 {
		return strings.Join(scopes, " ")
	}

	return firstNonEmpty(c.Scope, envValue(c.Environment, IdentityScopeEnv), DefaultScope)
}

type EnvironmentTokenCredential struct {
	VariableNames []string
	Environment   map[string]string
}

func NewEnvironmentTokenCredential(variableNames []string) *EnvironmentTokenCredential {
	if len(variableNames) == 0 {
		variableNames = []string{
			"CLOUDSHELL_CONFIGURATION_TOKEN",
			"CLOUDSHELL_SECRETS_TOKEN",
			"CLOUDSHELL_CONTROL_PLANE_TOKEN",
			"CLOUDSHELL_TOKEN",
		}
	}

	return &EnvironmentTokenCredential{VariableNames: variableNames}
}

func (c *EnvironmentTokenCredential) GetToken(ctx context.Context, scopes []string) (AccessToken, error) {
	for _, variableName := range c.VariableNames {
		if token := strings.TrimSpace(envValue(c.Environment, variableName)); token != "" {
			return AccessToken{Token: token}, nil
		}
	}

	return AccessToken{}, ErrCredentialUnavailable
}

type StaticTokenCredential struct {
	Token string
}

func (c StaticTokenCredential) GetToken(ctx context.Context, scopes []string) (AccessToken, error) {
	if strings.TrimSpace(c.Token) == "" {
		return AccessToken{}, ErrCredentialUnavailable
	}

	return AccessToken{Token: strings.TrimSpace(c.Token)}, nil
}

type ProfileCredentialOptions struct {
	ConfigDirectory string
	ConfigPath      string
	ProfileName     string
	Environment     map[string]string
}

type ProfileCredential struct {
	Options ProfileCredentialOptions
}

func NewProfileCredential(options ProfileCredentialOptions) *ProfileCredential {
	return &ProfileCredential{Options: options}
}

func (c *ProfileCredential) GetToken(ctx context.Context, scopes []string) (AccessToken, error) {
	configPath := c.resolveConfigPath()
	content, err := os.ReadFile(configPath)
	if errors.Is(err, os.ErrNotExist) {
		return AccessToken{}, ErrCredentialUnavailable
	}
	if err != nil {
		return AccessToken{}, err
	}

	var config profileConfig
	if err := json.Unmarshal(content, &config); err != nil {
		return AccessToken{}, err
	}

	profileName := firstNonEmpty(c.Options.ProfileName, envValue(c.Options.Environment, ProfileEnv), config.ActiveProfile)
	if profileName == "" {
		return AccessToken{}, ErrCredentialUnavailable
	}

	profile, ok := findProfile(config.Profiles, profileName)
	if !ok || !strings.EqualFold(profile.Credential.Kind, "staticBearer") {
		return AccessToken{}, ErrCredentialUnavailable
	}

	if profile.Credential.ExpiresOn != "" {
		expiresOn, err := time.Parse(time.RFC3339, profile.Credential.ExpiresOn)
		if err != nil {
			return AccessToken{}, err
		}
		if !expiresOn.After(time.Now()) {
			return AccessToken{}, ErrCredentialUnavailable
		}
	}

	token := strings.TrimSpace(profile.Credential.AccessToken)
	if token == "" && strings.TrimSpace(profile.Credential.AccessTokenPath) != "" {
		tokenPath := profile.Credential.AccessTokenPath
		if !filepath.IsAbs(tokenPath) {
			tokenPath = filepath.Join(filepath.Dir(configPath), tokenPath)
		}

		tokenBytes, err := os.ReadFile(tokenPath)
		if errors.Is(err, os.ErrNotExist) {
			return AccessToken{}, ErrCredentialUnavailable
		}
		if err != nil {
			return AccessToken{}, err
		}
		token = strings.TrimSpace(string(tokenBytes))
	}

	if token == "" {
		return AccessToken{}, ErrCredentialUnavailable
	}

	accessToken := AccessToken{Token: token}
	if profile.Credential.ExpiresOn != "" {
		accessToken.ExpiresOn, _ = time.Parse(time.RFC3339, profile.Credential.ExpiresOn)
	}
	return accessToken, nil
}

func (c *ProfileCredential) resolveConfigPath() string {
	if c.Options.ConfigPath != "" {
		return c.Options.ConfigPath
	}

	directory := firstNonEmpty(
		c.Options.ConfigDirectory,
		envValue(c.Options.Environment, ConfigDirectoryEnv),
		filepath.Join(userHomeDir(), ".cloudshell"))
	return filepath.Join(directory, "config.json")
}

type identityTokenResponse struct {
	AccessToken string `json:"access_token"`
	ExpiresIn   int    `json:"expires_in"`
}

type profileConfig struct {
	ActiveProfile string                   `json:"activeProfile"`
	Profiles      map[string]profileRecord `json:"profiles"`
}

type profileRecord struct {
	ControlPlane string                  `json:"controlPlane"`
	Environment  string                  `json:"environment"`
	Credential   profileCredentialRecord `json:"credential"`
}

type profileCredentialRecord struct {
	Kind            string `json:"kind"`
	AccessToken     string `json:"accessToken"`
	AccessTokenPath string `json:"accessTokenPath"`
	ExpiresOn       string `json:"expiresOn"`
}

func findProfile(profiles map[string]profileRecord, profileName string) (profileRecord, bool) {
	if profile, ok := profiles[profileName]; ok {
		return profile, true
	}

	for name, profile := range profiles {
		if strings.EqualFold(name, profileName) {
			return profile, true
		}
	}

	return profileRecord{}, false
}

func envValue(environment map[string]string, name string) string {
	if environment != nil {
		return environment[name]
	}

	return os.Getenv(name)
}

func firstNonEmpty(values ...string) string {
	for _, value := range values {
		if strings.TrimSpace(value) != "" {
			return strings.TrimSpace(value)
		}
	}

	return ""
}

func userHomeDir() string {
	if home, err := os.UserHomeDir(); err == nil && home != "" {
		return home
	}

	return "."
}

func stringsToErrors(values []string) []error {
	result := make([]error, 0, len(values))
	for _, value := range values {
		result = append(result, errors.New(value))
	}

	return result
}
