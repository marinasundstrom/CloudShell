using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using ResourceGraphState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.ResourceModel.Tests;

public sealed class JavaScriptAppServiceDiscoveryEnvironmentResolverTests
{
    [Fact]
    public async Task ResolveAsync_DerivesServiceDiscoveryVariablesFromReferencedEndpoints()
    {
        const string apiResourceId = "application.javascript-app:orders-api";
        var apiState = new ResourceGraphState(
            "orders-api",
            JavaScriptAppResourceTypeProvider.ResourceTypeId,
            ResourceId: apiResourceId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [JavaScriptAppResourceTypeProvider.Attributes.ServiceDiscoveryName] =
                    "orders-service",
                [JavaScriptAppResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "http",
                            "http",
                            Host: "127.0.0.1",
                            Port: 5174,
                            Exposure: "Local")
                    })
            });
        var frontend = CreateJavaScriptApp(
            "frontend",
            ResourceReference.ReferenceResourceId(
                apiResourceId,
                typeId: JavaScriptAppResourceTypeProvider.ResourceTypeId));
        var resolver = new JavaScriptAppServiceDiscoveryEnvironmentResolver(
            new ResourceGraphModel(new InMemoryResourceStateProvider([apiState])));

        var variables = await resolver.ResolveAsync(frontend);

        Assert.Equal(
            "http://127.0.0.1:5174",
            variables["services__orders_service__http__0"]);
        Assert.Equal(
            "http://127.0.0.1:5174",
            variables["services__orders_api__http__0"]);
        Assert.Equal(
            "http://127.0.0.1:5174",
            variables["services__application_javascript_app_orders_api__http__0"]);
    }

    [Fact]
    public async Task ResolveAsync_DerivesConfigurationAndSecretsClientVariablesFromReferences()
    {
        const string configurationResourceId = "configuration.store:javascript-container-app-settings";
        const string secretsResourceId = "secrets.vault:javascript-container-app-secrets";
        var configurationState = new ResourceGraphState(
            "javascript-container-app-settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ResourceId: configurationResourceId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] =
                    "http://127.0.0.1:5102"
            });
        var secretsState = new ResourceGraphState(
            "javascript-container-app-secrets",
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            ResourceId: secretsResourceId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] =
                    "http://127.0.0.1:6102"
            });
        var app = CreateJavaScriptApp(
            "javascript-container-frontend",
            ResourceReference.ReferenceResourceId(
                configurationResourceId,
                typeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId),
            ResourceReference.ReferenceResourceId(
                secretsResourceId,
                typeId: SecretsVaultResourceTypeProvider.ResourceTypeId));
        var resolver = new JavaScriptAppServiceDiscoveryEnvironmentResolver(
            new ResourceGraphModel(new InMemoryResourceStateProvider(
            [
                configurationState,
                secretsState
            ])));

        var variables = await resolver.ResolveAsync(app);

        Assert.Equal(
            "javascript-container-app-settings",
            variables["CLOUDSHELL_CONFIGURATION_SERVICE_NAME"]);
        Assert.Equal(
            configurationResourceId,
            variables["CLOUDSHELL_CONFIGURATION_JAVASCRIPT_CONTAINER_APP_SETTINGS_STORE_ID"]);
        Assert.Equal(
            "http://127.0.0.1:5102/api/configuration/stores/configuration.store%3Ajavascript-container-app-settings/settings",
            variables["CLOUDSHELL_CONFIGURATION_JAVASCRIPT_CONTAINER_APP_SETTINGS_ENDPOINT"]);
        Assert.Equal(
            "javascript-container-app-secrets",
            variables["CLOUDSHELL_SECRETS_VAULT_NAME"]);
        Assert.Equal(
            secretsResourceId,
            variables["CLOUDSHELL_SECRETS_JAVASCRIPT_CONTAINER_APP_SECRETS_VAULT_ID"]);
        Assert.Equal(
            "http://127.0.0.1:6102/api/secrets/vaults/secrets.vault%3Ajavascript-container-app-secrets/secrets",
            variables["CLOUDSHELL_SECRETS_JAVASCRIPT_CONTAINER_APP_SECRETS_ENDPOINT"]);
    }

    [Fact]
    public async Task ResolveAsync_DerivesClientVariablesForContainerProjectedJavaScriptApps()
    {
        const string configurationResourceId = "configuration.store:javascript-container-app-settings";
        var configurationState = new ResourceGraphState(
            "javascript-container-app-settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ResourceId: configurationResourceId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] =
                    "http://127.0.0.1:5102"
            });
        var app = CreateContainerProjectedJavaScriptApp(
            "javascript-container-frontend",
            ResourceReference.ReferenceResourceId(
                configurationResourceId,
                typeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId));
        var resolver = new JavaScriptAppServiceDiscoveryEnvironmentResolver(
            new ResourceGraphModel(new InMemoryResourceStateProvider([configurationState])));

        var variables = await resolver.ResolveAsync(app);

        Assert.Equal(
            "javascript-container-app-settings",
            variables["CLOUDSHELL_CONFIGURATION_SERVICE_NAME"]);
        Assert.Equal(
            "http://127.0.0.1:5102/api/configuration/stores/configuration.store%3Ajavascript-container-app-settings/settings",
            variables["CLOUDSHELL_CONFIGURATION_JAVASCRIPT_CONTAINER_APP_SETTINGS_ENDPOINT"]);
    }

    private static Resource CreateJavaScriptApp(
        string name,
        params ResourceReference[] references)
    {
        var state = new ResourceGraphState(
            name,
            JavaScriptAppResourceTypeProvider.ResourceTypeId,
            ResourceId: $"application.javascript-app:{name}",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [JavaScriptAppResourceTypeProvider.Attributes.References] =
                    ResourceAttributeValue.FromObject(references)
            });

        var resolver = new ResourceResolver(
            [JavaScriptAppResourceTypeProvider.ClassDefinition],
            [new JavaScriptAppResourceTypeProvider().TypeDefinition],
            attributeValueShapeProviders:
            [
                new NetworkingEndpointShapeProvider()
            ]);

        return resolver.Resolve(state);
    }

    private static Resource CreateContainerProjectedJavaScriptApp(
        string name,
        params ResourceReference[] references)
    {
        var state = new ResourceGraphState(
            name,
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ResourceId: $"application.container-app:{name}",
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] =
                    "cloudshell-javascript-container-frontend:dev",
                [JavaScriptAppResourceTypeProvider.Attributes.References] =
                    ResourceAttributeValue.FromObject(references)
            });
        var resolver = new ResourceResolver(
            [
                ContainerApplicationResourceTypeProvider.ClassDefinition,
                JavaScriptAppResourceTypeProvider.ClassDefinition
            ],
            [
                new ContainerApplicationResourceTypeProvider().TypeDefinition,
                new JavaScriptAppResourceTypeProvider().TypeDefinition
            ],
            attributeValueShapeProviders:
            [
                new NetworkingEndpointShapeProvider()
            ]);

        return resolver.Resolve(state);
    }
}
