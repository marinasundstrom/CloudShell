package main

import (
	"context"
	"encoding/json"
	"log"
	"net/http"
	"os"
	"sort"
	"strings"
	"time"

	cloudshell "github.com/cloudshell/sdk-go/cloudshell"
)

func main() {
	port := firstNonEmpty(os.Getenv("PORT"), "8080")

	mux := http.NewServeMux()
	mux.HandleFunc("/", handleIndex)
	mux.HandleFunc("/healthz", handleHealth)
	mux.HandleFunc("/alive", handleHealth)
	mux.HandleFunc("/environment", handleEnvironment)
	mux.HandleFunc("/configuration", handleConfiguration)

	log.Printf("Go container API listening on :%s", port)
	if err := http.ListenAndServe(":"+port, mux); err != nil {
		log.Fatal(err)
	}
}

func handleIndex(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, map[string]string{
		"message":  "Hello from the CloudShell Go container app sample",
		"resource": os.Getenv("CLOUDSHELL_RESOURCE_ID"),
	})
}

func handleConfiguration(w http.ResponseWriter, r *http.Request) {
	configuration, err := cloudshell.ConfigurationStoreFromEnvironment("", nil)
	if err != nil {
		http.Error(w, err.Error(), http.StatusServiceUnavailable)
		return
	}
	secrets, err := cloudshell.SecretsVaultFromEnvironment("", nil)
	if err != nil {
		http.Error(w, err.Error(), http.StatusServiceUnavailable)
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), defaultRequestTimeout)
	defer cancel()

	setting, err := configuration.GetSetting(ctx, "Sample--Message")
	if err != nil {
		http.Error(w, err.Error(), http.StatusBadGateway)
		return
	}
	secret, err := secrets.GetSecret(ctx, "Sample--ApiKey")
	if err != nil {
		http.Error(w, err.Error(), http.StatusBadGateway)
		return
	}

	message := ""
	if setting != nil {
		message = setting.Value
	}
	secretName := ""
	if secret != nil {
		secretName = secret.Name
	}

	writeJSON(w, map[string]any{
		"source":       "cloudshell-sdk",
		"message":      message,
		"hasApiKey":    secret != nil && strings.TrimSpace(secret.Value) != "",
		"secretName":   secretName,
		"resourceId":   os.Getenv("CLOUDSHELL_RESOURCE_ID"),
		"resourceName": os.Getenv("CLOUDSHELL_RESOURCE_NAME"),
	})
}

func handleHealth(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, map[string]string{
		"status": "ok",
	})
}

func handleEnvironment(w http.ResponseWriter, r *http.Request) {
	values := map[string]string{}
	for _, entry := range os.Environ() {
		name, value, ok := strings.Cut(entry, "=")
		if !ok {
			continue
		}

		if strings.HasPrefix(name, "CLOUDSHELL_") ||
			strings.HasPrefix(name, "services__") ||
			name == "PORT" ||
			name == "OTEL_SERVICE_NAME" {
			values[name] = value
		}
	}

	keys := make([]string, 0, len(values))
	for key := range values {
		keys = append(keys, key)
	}
	sort.Strings(keys)

	ordered := make([]map[string]string, 0, len(keys))
	for _, key := range keys {
		ordered = append(ordered, map[string]string{
			"name":  key,
			"value": values[key],
		})
	}

	writeJSON(w, ordered)
}

func writeJSON(w http.ResponseWriter, value any) {
	w.Header().Set("content-type", "application/json")
	if err := json.NewEncoder(w).Encode(value); err != nil {
		http.Error(w, err.Error(), http.StatusInternalServerError)
	}
}

func firstNonEmpty(values ...string) string {
	for _, value := range values {
		if strings.TrimSpace(value) != "" {
			return value
		}
	}

	return ""
}

const defaultRequestTimeout = 10 * time.Second
