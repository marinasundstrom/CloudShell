import com.cloudshell.launcher.CloudShellApp;
import com.cloudshell.launcher.CloudShellLauncherOptions;
import com.cloudshell.launcher.ConfigurationStoreResource;
import com.cloudshell.launcher.NetworkResource;
import com.cloudshell.launcher.SecretsVaultResource;
import java.nio.file.Files;
import java.nio.file.Path;

public final class AppHost {
    private AppHost() {
    }

    public static void main(String[] args) throws Exception {
        Path repoRoot = findRepositoryRoot(Path.of("").toAbsolutePath());
        Path sampleRoot = repoRoot.resolve("samples").resolve("JavaContainerApp");
        Path javaAppRoot = sampleRoot.resolve("App");
        Path cliProject = pathFromEnv(
                "CLOUDSHELL_CLI_PROJECT",
                repoRoot.resolve("CloudShell.Cli").resolve("CloudShell.Cli.csproj"));
        Path hostProject = pathFromEnv(
                "CLOUDSHELL_HOST_PROJECT",
                repoRoot.resolve("CloudShell.LocalDevelopmentHost").resolve("CloudShell.LocalDevelopmentHost.csproj"));
        Path stateDir = pathFromEnv(
                "CLOUDSHELL_STATE_DIR",
                sampleRoot.resolve(".cloudshell"));
        Path dataDir = Path.of(argumentValue(args, "--data-dir",
                System.getenv().getOrDefault("CLOUDSHELL_DATA_DIR", stateDir.toString())));
        String controlPlaneUrl = argumentValue(args, "--control-plane",
                System.getenv().getOrDefault("CLOUDSHELL_CONTROL_PLANE_URL", "http://127.0.0.1:5112"));

        CloudShellApp app = buildTemplate(repoRoot, javaAppRoot);

        if (hasArgument(args, "--apply") || hasArgument(args, "--start") || hasArgument(args, "--run")) {
            CloudShellLauncherOptions options = new CloudShellLauncherOptions()
                    .withCliProject(cliProject)
                    .withHostProject(hostProject)
                    .withControlPlaneUrl(controlPlaneUrl)
                    .withHostUrl(controlPlaneUrl)
                    .withStateDirectory(stateDir)
                    .withDataDirectory(dataDir)
                    .withTemplatePath(argumentPath(args, "--template-path", stateDir.resolve("resources.json")))
                    .withWorkingDirectory(repoRoot)
                    .withNoBuild(hasArgument(args, "--no-build"));

            if (hasArgument(args, "--run")) {
                System.exit(app.run(options).exitCode());
            }

            if (hasArgument(args, "--start")) {
                System.exit(app.start(options).exitCode());
            }

            System.exit(app.apply(options).exitCode());
        }

        System.out.print(app.toJson());
    }

    private static CloudShellApp buildTemplate(Path repoRoot, Path javaAppRoot) {
        CloudShellApp app = CloudShellApp.create("java-container-app")
                .withEnvironmentId("local")
                .withMetadata("cloudshell.source", "java")
                .withMetadata("cloudshell.sample", "JavaContainerApp");

        NetworkResource hostNetwork = app.addNetwork("host")
                .withResourceId("network:host")
                .withDisplayName("Host network")
                .withNetworkKind("Host")
                .withHostReadiness("hostReady");

        ConfigurationStoreResource settings = app.addConfigurationStore("java-container-settings")
                .withDisplayName("Java Container Settings")
                .withEndpoint("http://localhost:5113")
                .withSeed(seed -> seed.setting("Sample--Message", "Hello from Java container app configuration"));

        SecretsVaultResource secrets = app.addSecretsVault("java-container-secrets")
                .withDisplayName("Java Container Secrets")
                .withEndpoint("http://localhost:6113")
                .withSeed(seed -> seed.secret("Sample--ApiKey", "java-container-secret", "v1"));

        var api = app.addJavaApp(
                "java-container-api",
                javaAppRoot.toString(),
                "target/cloudshell-java-container-app-sample.jar")
                .withDisplayName("Java Container API")
                .withServiceDiscovery()
                .withEnvironmentVariable("PORT", "8080")
                .withEnvironmentVariable("OTEL_SERVICE_NAME", "java-container-api")
                .withEnvironmentVariable("CLOUDSHELL_CONFIGURATION_SERVICE_NAME", "java-container-settings")
                .withEnvironmentVariable("CLOUDSHELL_SECRETS_VAULT_NAME", "java-container-secrets")
                .withReference(settings)
                .withReference(secrets)
                .dependsOn(settings)
                .dependsOn(secrets)
                .withHttpEndpoint("localhost", 5191, 8080, hostNetwork)
                .withHttpHealthCheck("/healthz")
                .withHttpLivenessCheck("/alive")
                .withDefaultConsoleLogSource()
                .asContainerApp(
                    null,
                    null,
                    "dev",
                    repoRoot.toString(),
                    "samples/JavaContainerApp/App/Dockerfile",
                    2)
                .requireIdentity("java-container-api")
                .provisionIdentityOnStartup();

        settings.allowResourceIdentity(
            api,
            "CloudShell.Configuration/stores/settings/read/action",
            "java-container-api");
        secrets.allowResourceIdentity(
            api,
            "CloudShell.Secrets/vaults/secrets/read/action",
            "java-container-api");

        return app;
    }

    private static Path pathFromEnv(String name, Path fallback) {
        String value = System.getenv(name);
        return value == null || value.isBlank() ? fallback : Path.of(value);
    }

    private static Path argumentPath(String[] args, String name, Path fallback) {
        String value = argumentValue(args, name, null);
        return value == null || value.isBlank() ? fallback : Path.of(value);
    }

    private static boolean hasArgument(String[] args, String name) {
        for (String arg : args) {
            if (arg.equalsIgnoreCase(name)) {
                return true;
            }
        }

        return false;
    }

    private static String argumentValue(String[] args, String name, String fallback) {
        for (int index = 0; index < args.length - 1; index++) {
            if (args[index].equalsIgnoreCase(name)) {
                return args[index + 1];
            }
        }

        return fallback;
    }

    private static Path findRepositoryRoot(Path start) {
        Path directory = start;
        while (directory != null) {
            if (Files.exists(directory.resolve("CloudShell.slnx"))) {
                return directory;
            }

            directory = directory.getParent();
        }

        throw new IllegalStateException("Could not find CloudShell.slnx from " + start);
    }
}
