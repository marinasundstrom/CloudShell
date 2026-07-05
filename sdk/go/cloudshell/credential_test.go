package cloudshell

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"net/url"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestIdentityCredentialRequestsClientCredentialsToken(t *testing.T) {
	var form url.Values
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodPost {
			t.Fatalf("expected POST, got %s", r.Method)
		}
		if err := r.ParseForm(); err != nil {
			t.Fatal(err)
		}
		form = r.Form
		_ = json.NewEncoder(w).Encode(map[string]any{
			"access_token": "identity-token",
			"expires_in":   3600,
		})
	}))
	defer server.Close()

	credential := NewIdentityCredential(&IdentityCredential{
		TokenEndpoint: server.URL,
		ClientID:      "application:api/api-service",
		ClientSecret:  "local-development-secret",
		HTTPClient:    server.Client(),
	})

	token, err := credential.GetToken(context.Background(), []string{"ControlPlane.Access"})
	if err != nil {
		t.Fatal(err)
	}

	if token.Token != "identity-token" {
		t.Fatalf("expected identity-token, got %q", token.Token)
	}
	if form.Get("grant_type") != "client_credentials" ||
		form.Get("client_id") != "application:api/api-service" ||
		form.Get("client_secret") != "local-development-secret" ||
		form.Get("scope") != "ControlPlane.Access" {
		t.Fatalf("unexpected form: %#v", form)
	}
}

func TestDefaultCredentialPrefersIdentityToken(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_ = json.NewEncoder(w).Encode(map[string]any{
			"access_token": "identity-token",
		})
	}))
	defer server.Close()

	credential := &DefaultCredential{
		Credentials: []TokenCredential{
			NewIdentityCredential(&IdentityCredential{
				TokenEndpoint: server.URL,
				ClientID:      "application:api/api-service",
				ClientSecret:  "local-development-secret",
				HTTPClient:    server.Client(),
			}),
			&EnvironmentTokenCredential{
				VariableNames: []string{"CLOUDSHELL_TOKEN"},
				Environment: map[string]string{
					"CLOUDSHELL_TOKEN": "environment-token",
				},
			},
		},
	}

	token, err := credential.GetToken(context.Background(), []string{"ControlPlane.Access"})
	if err != nil {
		t.Fatal(err)
	}

	if token.Token != "identity-token" {
		t.Fatalf("expected identity-token, got %q", token.Token)
	}
}

func TestProfileCredentialReadsRelativeTokenFile(t *testing.T) {
	directory := t.TempDir()
	if err := os.Mkdir(filepath.Join(directory, "tokens"), 0o700); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(directory, "tokens", "local.token"), []byte("file-token\n"), 0o600); err != nil {
		t.Fatal(err)
	}

	config := `{
		"activeProfile": "local",
		"profiles": {
			"local": {
				"credential": {
					"kind": "staticBearer",
					"accessTokenPath": "tokens/local.token",
					"expiresOn": "` + time.Now().Add(time.Hour).UTC().Format(time.RFC3339) + `"
				}
			}
		}
	}`
	if err := os.WriteFile(filepath.Join(directory, "config.json"), []byte(config), 0o600); err != nil {
		t.Fatal(err)
	}

	credential := NewProfileCredential(ProfileCredentialOptions{
		ConfigDirectory: directory,
	})

	token, err := credential.GetToken(context.Background(), nil)
	if err != nil {
		t.Fatal(err)
	}

	if token.Token != "file-token" {
		t.Fatalf("expected file-token, got %q", token.Token)
	}
}
