using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class SqlServerCredentialNamesTests
{
    [Fact]
    public void CreateManagedUserNameFromPrincipalSubject_MatchesResourceIdentityGrantName()
    {
        var grant = new ResourcePermissionGrant(
            ResourcePrincipalReference.ForResourceIdentity("application:api", "api"),
            "application:sql",
            "database.readWrite");

        var grantUserName = SqlServerCredentialNames.CreateManagedUserName(grant);
        var subjectUserName = SqlServerCredentialNames.CreateManagedUserNameFromPrincipalSubject(
            "application:api/api",
            "application:sql",
            "database.readWrite");

        Assert.Equal(grantUserName, subjectUserName);
        Assert.StartsWith("cloudshell_", subjectUserName);
    }

    [Fact]
    public void CreateCredentialPassword_ReturnsSqlServerCompatibleSecret()
    {
        var password = SqlServerCredentialNames.CreateCredentialPassword();

        Assert.StartsWith("Cs1!", password);
        Assert.DoesNotContain('+', password);
        Assert.DoesNotContain('/', password);
    }
}
