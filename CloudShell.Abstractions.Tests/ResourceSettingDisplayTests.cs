using CloudShell.Abstractions.ResourceManager;

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
}
