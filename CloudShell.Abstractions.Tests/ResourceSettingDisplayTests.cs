using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceSettingDisplayTests
{
    [Fact]
    public void Format_ReturnsLiteralEnvironmentVariableValue()
    {
        var value = ResourceSettingDisplay.Format(
            new EnvironmentVariableAssignment("ASPNETCORE_ENVIRONMENT", "Development"));

        Assert.Equal("Development", value);
    }

    [Fact]
    public void Format_ReturnsConfigurationReference()
    {
        var value = ResourceSettingDisplay.Format(
            EnvironmentVariableAssignment.FromConfiguration(
                "ConnectionStrings__Default",
                new ConfigurationEntryReference(
                    "configuration:app",
                    "ConnectionStrings:Default",
                    "v2")));

        Assert.Equal(
            "@CloudShell.Configuration(storeResourceId=configuration:app; entryName=ConnectionStrings:Default; version=v2)",
            value);
    }

    [Fact]
    public void Format_ReturnsSecretReferenceWithoutSecretValue()
    {
        var value = ResourceSettingDisplay.Format(
            EnvironmentVariableAssignment.FromSecret(
                "DB_PASSWORD",
                new SecretReference(
                    "secrets-vault:app",
                    "db-password",
                    "2026-06-13")));

        Assert.Equal(
            "@CloudShell.Secret(vaultResourceId=secrets-vault:app; secretName=db-password; version=2026-06-13)",
            value);
    }

    [Fact]
    public void Format_ReturnsAppSettingReference()
    {
        var value = ResourceSettingDisplay.Format(
            AppSetting.FromConfiguration(
                "Database:Host",
                new ConfigurationEntryReference("configuration:app", "database-host")));

        Assert.Equal(
            "@CloudShell.Configuration(storeResourceId=configuration:app; entryName=database-host)",
            value);
    }

    [Fact]
    public void ResourceSettingResolutionException_IncludesReferenceContext()
    {
        var exception = new ResourceSettingResolutionException(
            "SAMPLE_API_KEY",
            "secret",
            "Identity 'application:api/api-service' is not allowed to read secrets.");

        Assert.Equal("SAMPLE_API_KEY", exception.SettingName);
        Assert.Equal("secret", exception.ReferenceKind);
        Assert.Equal(
            "Could not resolve secret reference for setting 'SAMPLE_API_KEY'. Identity 'application:api/api-service' is not allowed to read secrets.",
            exception.Message);
    }

    [Fact]
    public void ApplicationSettingReferenceDisplay_ReturnsLiteralStatus()
    {
        var row = ApplicationSettingReferenceDisplay.Create(
            AppSetting.Literal("Sample:Mode", "Development"),
            "application:api",
            null,
            _ => null);

        Assert.Equal("Sample:Mode", row.Name);
        Assert.Equal("Literal value", row.Source);
        Assert.Equal("Development", row.Target);
        Assert.Equal("Visible", row.Status);
        Assert.Equal("ok", row.StatusKind);
        Assert.Null(row.Detail);
    }

    [Fact]
    public void ApplicationSettingReferenceDisplay_FlagsMissingConfigurationStore()
    {
        var row = ApplicationSettingReferenceDisplay.Create(
            AppSetting.FromConfiguration(
                "Sample:Message",
                new ConfigurationEntryReference("configuration:missing", "Sample:Message")),
            "application:api",
            null,
            _ => null);

        Assert.Equal("Configuration entry", row.Source);
        Assert.Equal("configuration:missing / Sample:Message", row.Target);
        Assert.Equal("Unavailable", row.Status);
        Assert.Equal("warning", row.StatusKind);
        Assert.Equal("configuration:missing (unavailable)", row.Detail);
    }

    [Fact]
    public void ApplicationSettingReferenceDisplay_ShowsGrantRequirementForIdentityBoundSecret()
    {
        var vault = CreateResource("secrets-vault:app", "App Secrets", ResourceClass.SecretsVault);
        var identity = new ResourceIdentityBinding("identity:development", Name: "api-service");

        var row = ApplicationSettingReferenceDisplay.Create(
            EnvironmentVariableAssignment.FromSecret(
                "SAMPLE_API_KEY",
                new SecretReference("secrets-vault:app", "sample-api-key", "v2")),
            "application:api",
            identity,
            id => string.Equals(id, vault.Id, StringComparison.OrdinalIgnoreCase) ? vault : null);

        Assert.Equal("Secret reference", row.Source);
        Assert.Equal("App Secrets / sample-api-key", row.Target);
        Assert.Equal("Grant required", row.Status);
        Assert.Equal("warning", row.StatusKind);
        Assert.Contains("secrets-vault:app; version v2", row.Detail);
        Assert.Contains(SecretsVaultResourceOperationPermissions.ReadSecrets, row.Detail);
        Assert.Contains("application:api/api-service", row.Detail);
    }

    [Fact]
    public void ApplicationSettingReferenceDisplay_ShowsGrantedStatusForIdentityBoundSecret()
    {
        var vault = CreateResource("secrets-vault:app", "App Secrets", ResourceClass.SecretsVault);
        var identity = new ResourceIdentityBinding("identity:development", Name: "api-service");
        var evaluator = new ResourcePermissionGrantEvaluator(
        [
            new ResourcePermissionGrant(
                ResourceIdentityReference.ForResource("application:api", "api-service"),
                "secrets-vault:app",
                SecretsVaultResourceOperationPermissions.ReadSecrets)
        ]);

        var row = ApplicationSettingReferenceDisplay.Create(
            EnvironmentVariableAssignment.FromSecret(
                "SAMPLE_API_KEY",
                new SecretReference("secrets-vault:app", "sample-api-key", "v2")),
            "application:api",
            identity,
            id => string.Equals(id, vault.Id, StringComparison.OrdinalIgnoreCase) ? vault : null,
            evaluator);

        Assert.Equal("Secret reference", row.Source);
        Assert.Equal("App Secrets / sample-api-key", row.Target);
        Assert.Equal("Granted", row.Status);
        Assert.Equal("ok", row.StatusKind);
        Assert.Contains("secrets-vault:app; version v2", row.Detail);
        Assert.Contains(SecretsVaultResourceOperationPermissions.ReadSecrets, row.Detail);
        Assert.Contains("application:api/api-service", row.Detail);
    }

    private static Resource CreateResource(
        string id,
        string name,
        ResourceClass resourceClass = ResourceClass.Generic) =>
        new(
            id,
            name,
            id,
            "test",
            "local",
            ResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: id,
            ResourceClass: resourceClass);
}
