using CloudShell.Client.Authentication;

namespace CloudShell.Cli.Tests;

public sealed class CliCredentialResolverTests
{
    [Fact]
    public async Task ResolveBearerTokenAsync_PrefersExplicitBearerToken()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, CloudShellProfileCredential.DefaultConfigFileName),
                """
                {
                  "activeProfile": "local",
                  "profiles": {
                    "local": {
                      "credential": {
                        "kind": "staticBearer",
                        "accessToken": "profile-token"
                      }
                    }
                  }
                }
                """);

            var token = await CliCredentialResolver.ResolveBearerTokenAsync(
                " explicit-token ",
                new CloudShellProfileCredentialOptions
                {
                    ConfigDirectory = directory
                },
                CancellationToken.None);

            Assert.Equal("explicit-token", token);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_ReadsProfileTokenWhenBearerTokenMissing()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, CloudShellProfileCredential.DefaultConfigFileName),
                """
                {
                  "activeProfile": "local",
                  "profiles": {
                    "local": {
                      "credential": {
                        "kind": "staticBearer",
                        "accessToken": "profile-token"
                      }
                    }
                  }
                }
                """);

            var token = await CliCredentialResolver.ResolveBearerTokenAsync(
                null,
                new CloudShellProfileCredentialOptions
                {
                    ConfigDirectory = directory
                },
                CancellationToken.None);

            Assert.Equal("profile-token", token);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-cli-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
