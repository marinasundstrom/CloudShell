package cloudshell

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestConfigurationStoreClientSendsBearerTokenAndReadsSettings(t *testing.T) {
	var authorization string
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		authorization = r.Header.Get("authorization")
		_ = json.NewEncoder(w).Encode([]ConfigurationSetting{
			{Name: "Sample--Message", Value: "Hello"},
		})
	}))
	defer server.Close()

	client := NewConfigurationStoreClient(
		server.URL+"/settings",
		StaticTokenCredential{Token: "configuration-token"})
	client.HTTPClient = server.Client()

	settings, err := client.GetSettings(context.Background())
	if err != nil {
		t.Fatal(err)
	}

	if len(settings) != 1 || settings[0].Name != "Sample--Message" || settings[0].Value != "Hello" {
		t.Fatalf("unexpected settings: %#v", settings)
	}
	if authorization != "Bearer configuration-token" {
		t.Fatalf("unexpected authorization header: %q", authorization)
	}
}

func TestConfigurationStoreClientMapsPortableHierarchySeparator(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_ = json.NewEncoder(w).Encode([]ConfigurationSetting{
			{Name: "Orders--Api--BaseUrl", Value: "http://localhost:5080"},
		})
	}))
	defer server.Close()

	client := NewConfigurationStoreClient(
		server.URL+"/settings",
		StaticTokenCredential{Token: "configuration-token"})
	client.HTTPClient = server.Client()

	values, err := client.ToMap(context.Background(), true)
	if err != nil {
		t.Fatal(err)
	}

	if values["Orders:Api:BaseUrl"] != "http://localhost:5080" {
		t.Fatalf("unexpected values: %#v", values)
	}
}

func TestFindEndpointFiltersByServiceName(t *testing.T) {
	endpoint, ok := findEndpoint(
		"CLOUDSHELL_CONFIGURATION_",
		"app-settings",
		map[string]string{
			"CLOUDSHELL_CONFIGURATION_OTHER_ENDPOINT":        "http://localhost/other",
			"CLOUDSHELL_CONFIGURATION_APP_SETTINGS_ENDPOINT": "http://localhost/app-settings",
		})

	if !ok {
		t.Fatal("expected endpoint")
	}
	if endpoint != "http://localhost/app-settings" {
		t.Fatalf("unexpected endpoint: %s", endpoint)
	}
}
