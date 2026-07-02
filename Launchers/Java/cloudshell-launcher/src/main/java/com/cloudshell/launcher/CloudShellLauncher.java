package com.cloudshell.launcher;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;

public final class CloudShellLauncher {
    private CloudShellLauncher() {
    }

    public static CloudShellLauncherResult apply(
        CloudShellApp app,
        CloudShellLauncherOptions options)
            throws IOException, InterruptedException {
        Path templatePath = writeTemplate(app, options);
        Command command = buildTemplateApplyCommand(templatePath, options, false);
        int exitCode = runCommand(command, options);
        return new CloudShellLauncherResult(command.name(), command.arguments(), exitCode, templatePath);
    }

    public static CloudShellLauncherResult start(
        CloudShellApp app,
        CloudShellLauncherOptions options)
            throws IOException, InterruptedException {
        Path templatePath = writeTemplate(app, options);
        Command command = buildTemplateApplyCommand(templatePath, options, true);
        int exitCode = runCommand(command, options);
        return new CloudShellLauncherResult(command.name(), command.arguments(), exitCode, templatePath);
    }

    public static CloudShellLauncherResult run(
        CloudShellApp app,
        CloudShellLauncherOptions options)
            throws IOException, InterruptedException {
        Path templatePath = writeTemplate(app, options);
        String hostUrl = firstNonBlank(options.hostUrl(), options.controlPlaneUrl());
        if (isBlank(hostUrl)) {
            throw new IllegalArgumentException("A host URL or Control Plane URL is required for foreground run.");
        }

        Command hostCommand = buildHostRunCommand(options, hostUrl);
        Process host = startProcess(hostCommand, options);
        Thread shutdownHook = new Thread(() -> destroy(host));
        Runtime.getRuntime().addShutdownHook(shutdownHook);
        try {
            waitForReady(host, hostUrl, options.bearerToken(), options.timeoutSeconds());

            CloudShellLauncherOptions applyOptions = copyForApplyToForegroundHost(options, hostUrl);
            Command applyCommand = buildTemplateApplyCommand(templatePath, applyOptions, false);
            int applyExitCode = runCommand(applyCommand, applyOptions);
            if (applyExitCode != 0) {
                destroy(host);
                return new CloudShellLauncherResult(
                    applyCommand.name(),
                    applyCommand.arguments(),
                    applyExitCode,
                    templatePath);
            }

            int hostExitCode = host.waitFor();
            return new CloudShellLauncherResult(
                hostCommand.name(),
                hostCommand.arguments(),
                hostExitCode,
                templatePath);
        } catch (IOException | InterruptedException | RuntimeException ex) {
            destroy(host);
            throw ex;
        } finally {
            try {
                Runtime.getRuntime().removeShutdownHook(shutdownHook);
            } catch (IllegalStateException ex) {
                // JVM shutdown is already in progress.
            }
        }
    }

    public static Path writeTemplate(
        CloudShellApp app,
        CloudShellLauncherOptions options)
            throws IOException {
        Path templatePath = options.templatePath();
        if (templatePath == null) {
            Path directory = options.stateDirectory() == null
                ? Files.createTempDirectory("cloudshell-template-")
                : options.stateDirectory();
            templatePath = directory.resolve("resources.json");
        }

        Files.createDirectories(templatePath.toAbsolutePath().getParent());
        Files.writeString(templatePath, app.toJson(), StandardCharsets.UTF_8);
        return templatePath;
    }

    static Command buildTemplateApplyCommand(
        Path templatePath,
        CloudShellLauncherOptions options,
        boolean startHost) {
        List<String> applyArguments = new ArrayList<>();
        applyArguments.add("template");
        applyArguments.add("apply");
        applyArguments.add(templatePath.toString());
        addOption(applyArguments, "--control-plane", options.controlPlaneUrl());
        addOption(applyArguments, "--state-dir", options.stateDirectory());
        addOption(applyArguments, "--host-project", options.hostProject());
        addOption(applyArguments, "--data-dir", options.dataDirectory());
        addOption(applyArguments, "--url", options.hostUrl());
        addOption(applyArguments, "--timeout-seconds", Integer.toString(options.timeoutSeconds()));
        addOption(applyArguments, "--mode", options.mode());
        addOption(applyArguments, "--bearer-token", options.bearerToken());
        if (startHost) {
            applyArguments.add("--start");
        }

        if (options.noBuild()) {
            applyArguments.add("--no-build");
        }

        if (options.cliProject() == null) {
            return new Command(options.cloudshellCommand(), List.copyOf(applyArguments));
        }

        List<String> arguments = new ArrayList<>();
        arguments.add("run");
        arguments.add("--project");
        arguments.add(options.cliProject().toString());
        arguments.add("--");
        arguments.addAll(applyArguments);
        return new Command("dotnet", List.copyOf(arguments));
    }

    static Command buildHostRunCommand(
        CloudShellLauncherOptions options,
        String hostUrl) {
        if (options.hostProject() == null) {
            throw new IllegalArgumentException("A host project is required for foreground run.");
        }

        List<String> arguments = new ArrayList<>();
        arguments.add("run");
        arguments.add("--project");
        arguments.add(options.hostProject().toString());
        if (options.noBuild()) {
            arguments.add("--no-build");
        }

        arguments.add("--");
        arguments.add("--urls");
        arguments.add(hostUrl);
        if (options.dataDirectory() != null) {
            arguments.add("--CloudShell:DataDirectory");
            arguments.add(options.dataDirectory().toString());
        }

        return new Command("dotnet", List.copyOf(arguments));
    }

    private static CloudShellLauncherOptions copyForApplyToForegroundHost(
        CloudShellLauncherOptions options,
        String hostUrl) {
        return new CloudShellLauncherOptions()
            .withCliProject(options.cliProject())
            .withCloudShellCommand(options.cloudshellCommand())
            .withControlPlaneUrl(hostUrl)
            .withMode(options.mode())
            .withBearerToken(options.bearerToken())
            .withWorkingDirectory(options.workingDirectory())
            .withInheritIo(options.inheritIo());
    }

    private static int runCommand(Command command, CloudShellLauncherOptions options)
            throws IOException, InterruptedException {
        Process process = startProcess(command, options);
        return process.waitFor();
    }

    private static Process startProcess(Command command, CloudShellLauncherOptions options)
            throws IOException {
        ProcessBuilder builder = new ProcessBuilder();
        List<String> commandLine = new ArrayList<>();
        commandLine.add(command.name());
        commandLine.addAll(command.arguments());
        builder.command(commandLine);
        if (options.workingDirectory() != null) {
            builder.directory(options.workingDirectory().toFile());
        }

        if (options.inheritIo()) {
            builder.inheritIO();
        }

        return builder.start();
    }

    private static void waitForReady(
        Process host,
        String hostUrl,
        String bearerToken,
        int timeoutSeconds)
            throws IOException, InterruptedException {
        HttpClient client = HttpClient.newHttpClient();
        URI resourcesUri = URI.create(normalizeBaseUrl(hostUrl) + "api/control-plane/v1/resources");
        long deadline = System.nanoTime() + Duration.ofSeconds(timeoutSeconds).toNanos();
        while (System.nanoTime() < deadline) {
            if (!host.isAlive()) {
                throw new IllegalStateException("CloudShell host exited before it was ready.");
            }

            HttpRequest.Builder request = HttpRequest.newBuilder(resourcesUri).GET();
            if (!isBlank(bearerToken)) {
                request.header("Authorization", "Bearer " + bearerToken);
            }

            try {
                HttpResponse<Void> response = client.send(
                    request.build(),
                    HttpResponse.BodyHandlers.discarding());
                int status = response.statusCode();
                if (status == 200 || status == 204) {
                    return;
                }
            } catch (IOException ex) {
                // Host is still starting.
            }

            Thread.sleep(500);
        }

        throw new IllegalStateException("CloudShell host did not become ready within "
            + timeoutSeconds + " seconds.");
    }

    private static void destroy(Process process) {
        process.destroy();
        try {
            if (!process.waitFor(5, java.util.concurrent.TimeUnit.SECONDS)) {
                process.destroyForcibly();
            }
        } catch (InterruptedException ex) {
            Thread.currentThread().interrupt();
            process.destroyForcibly();
        }
    }

    private static void addOption(List<String> arguments, String name, Path value) {
        if (value != null) {
            arguments.add(name);
            arguments.add(value.toString());
        }
    }

    private static void addOption(List<String> arguments, String name, String value) {
        if (!isBlank(value)) {
            arguments.add(name);
            arguments.add(value.trim());
        }
    }

    private static String normalizeBaseUrl(String url) {
        return url.endsWith("/") ? url : url + "/";
    }

    private static String firstNonBlank(String first, String second) {
        return isBlank(first) ? second : first;
    }

    private static boolean isBlank(String value) {
        return value == null || value.isBlank();
    }

    record Command(String name, List<String> arguments) {
    }
}
