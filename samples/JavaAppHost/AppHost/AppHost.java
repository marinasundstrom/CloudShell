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
        Path sampleRoot = repoRoot.resolve("samples").resolve("JavaAppHost");
        Path javaAppRoot = repoRoot.resolve("samples").resolve("JavaApp").resolve("App");
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
                System.getenv().getOrDefault("CLOUDSHELL_CONTROL_PLANE_URL", "http://127.0.0.1:5100"));

        CloudShellApp app = buildTemplate(javaAppRoot);

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

    private static CloudShellApp buildTemplate(Path javaAppRoot) {
        CloudShellApp app = CloudShellApp.create("java-app-host")
                .withEnvironmentId("local")
                .withMetadata("cloudshell.source", "java")
                .withMetadata("cloudshell.sample", "JavaAppHost");

        NetworkResource hostNetwork = app.addNetwork("host")
                .withResourceId("network:host")
                .withDisplayName("Host network")
                .withNetworkKind("Host")
                .withHostReadiness("hostReady");

        ConfigurationStoreResource settings = app.addConfigurationStore("java-launcher-settings")
                .withDisplayName("Java Launcher Settings")
                .withEndpoint("http://localhost:5104")
                .withSetting("Sample--Message", "Hello from Java launcher seed");

        SecretsVaultResource secrets = app.addSecretsVault("java-launcher-secrets")
                .withDisplayName("Java Launcher Secrets")
                .withEndpoint("http://localhost:6104")
                .withSecret("Sample--ApiKey", "java-launcher-secret", "v1");

        app.addJavaApp(
                "java-launcher-api",
                javaAppRoot.toString(),
                "target/cloudshell-java-app-sample.jar")
                .withDisplayName("Java Launcher API")
                .withServiceDiscovery()
                .withEnvironmentVariable("PORT", "5186")
                .withEnvironmentVariable("Sample__Message", settings.entry("Sample--Message"))
                .withEnvironmentVariable("Sample__ApiKey", secrets.secret("Sample--ApiKey"))
                .withEnvironmentVariable("OTEL_SERVICE_NAME", "java-launcher-api")
                .withReference(settings)
                .withReference(secrets)
                .dependsOn(settings)
                .dependsOn(secrets)
                .withHttpEndpoint("localhost", 5186, 5186, hostNetwork)
                .withHttpHealthCheck("/healthz")
                .withHttpLivenessCheck("/alive")
                .withDefaultConsoleLogSource();

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
