package cloudshell

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestSecretsVaultClientSendsBearerTokenAndReadsSecrets(t *testing.T) {
	var authorization string
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		authorization = r.Header.Get("authorization")
		_ = json.NewEncoder(w).Encode([]SecretProperties{
			{Name: "Sample--ApiKey", Version: "v1"},
		})
	}))
	defer server.Close()

	client := NewSecretsVaultClient(
		server.URL+"/secrets",
		StaticTokenCredential{Token: "secrets-token"})
	client.HTTPClient = server.Client()

	secrets, err := client.GetSecrets(context.Background())
	if err != nil {
		t.Fatal(err)
	}

	if len(secrets) != 1 || secrets[0].Name != "Sample--ApiKey" || secrets[0].Version != "v1" {
		t.Fatalf("unexpected secrets: %#v", secrets)
	}
	if authorization != "Bearer secrets-token" {
		t.Fatalf("unexpected authorization header: %q", authorization)
	}
}

func TestSecretsVaultClientReadsVersionedSecret(t *testing.T) {
	var requestedPath string
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		requestedPath = r.URL.RequestURI()
		_ = json.NewEncoder(w).Encode(SecretValue{
			Name:    "Sample--ApiKey",
			Value:   "secret-value",
			Version: "v1",
		})
	}))
	defer server.Close()

	client := NewSecretsVaultClient(
		server.URL+"/secrets?resourceId=secrets%3Aapp",
		StaticTokenCredential{Token: "secrets-token"})
	client.HTTPClient = server.Client()

	secret, err := client.GetSecret(context.Background(), "Sample--ApiKey", "v1")
	if err != nil {
		t.Fatal(err)
	}

	if secret == nil || secret.Value != "secret-value" || secret.Version != "v1" {
		t.Fatalf("unexpected secret: %#v", secret)
	}
	if requestedPath != "/secrets/Sample--ApiKey?resourceId=secrets%3Aapp&version=v1" {
		t.Fatalf("unexpected request path: %s", requestedPath)
	}
}

func TestSecretsVaultClientReturnsNilForMissingSecret(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	}))
	defer server.Close()

	client := NewSecretsVaultClient(
		server.URL+"/secrets",
		StaticTokenCredential{Token: "secrets-token"})
	client.HTTPClient = server.Client()

	secret, err := client.GetSecret(context.Background(), "Missing")
	if err != nil {
		t.Fatal(err)
	}
	if secret != nil {
		t.Fatalf("expected nil secret, got %#v", secret)
	}
}

func TestSecretsVaultFromEnvironmentFiltersByVaultName(t *testing.T) {
	t.Setenv("CLOUDSHELL_SECRETS_OTHER_ENDPOINT", "http://localhost/other")
	t.Setenv("CLOUDSHELL_SECRETS_APP_VAULT_ENDPOINT", "http://localhost/app-vault")

	client, err := SecretsVaultFromEnvironment("app-vault", StaticTokenCredential{Token: "secrets-token"})
	if err != nil {
		t.Fatal(err)
	}

	if client.SecretsEndpoint != "http://localhost/app-vault" {
		t.Fatalf("unexpected endpoint: %s", client.SecretsEndpoint)
	}
}
