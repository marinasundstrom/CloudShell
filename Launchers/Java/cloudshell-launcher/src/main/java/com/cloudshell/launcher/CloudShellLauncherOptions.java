package com.cloudshell.launcher;

import java.nio.file.Path;

public final class CloudShellLauncherOptions {
    private Path cliProject;
    private String cloudshellCommand = "cloudshell";
    private Path templatePath;
    private String controlPlaneUrl;
    private Path stateDirectory;
    private Path dataDirectory;
    private Path hostProject;
    private String hostUrl;
    private boolean noBuild;
    private int timeoutSeconds = 60;
    private String mode = "create-or-update";
    private String bearerToken;
    private Path workingDirectory;
    private boolean inheritIo = true;

    public Path cliProject() {
        return cliProject;
    }

    public String cloudshellCommand() {
        return cloudshellCommand;
    }

    public Path templatePath() {
        return templatePath;
    }

    public String controlPlaneUrl() {
        return controlPlaneUrl;
    }

    public Path stateDirectory() {
        return stateDirectory;
    }

    public Path dataDirectory() {
        return dataDirectory;
    }

    public Path hostProject() {
        return hostProject;
    }

    public String hostUrl() {
        return hostUrl;
    }

    public boolean noBuild() {
        return noBuild;
    }

    public int timeoutSeconds() {
        return timeoutSeconds;
    }

    public String mode() {
        return mode;
    }

    public String bearerToken() {
        return bearerToken;
    }

    public Path workingDirectory() {
        return workingDirectory;
    }

    public boolean inheritIo() {
        return inheritIo;
    }

    public CloudShellLauncherOptions withCliProject(Path cliProject) {
        this.cliProject = cliProject;
        return this;
    }

    public CloudShellLauncherOptions withCloudShellCommand(String cloudshellCommand) {
        this.cloudshellCommand = cloudshellCommand;
        return this;
    }

    public CloudShellLauncherOptions withTemplatePath(Path templatePath) {
        this.templatePath = templatePath;
        return this;
    }

    public CloudShellLauncherOptions withControlPlaneUrl(String controlPlaneUrl) {
        this.controlPlaneUrl = controlPlaneUrl;
        return this;
    }

    public CloudShellLauncherOptions withStateDirectory(Path stateDirectory) {
        this.stateDirectory = stateDirectory;
        return this;
    }

    public CloudShellLauncherOptions withDataDirectory(Path dataDirectory) {
        this.dataDirectory = dataDirectory;
        return this;
    }

    public CloudShellLauncherOptions withHostProject(Path hostProject) {
        this.hostProject = hostProject;
        return this;
    }

    public CloudShellLauncherOptions withHostUrl(String hostUrl) {
        this.hostUrl = hostUrl;
        return this;
    }

    public CloudShellLauncherOptions withNoBuild(boolean noBuild) {
        this.noBuild = noBuild;
        return this;
    }

    public CloudShellLauncherOptions withTimeoutSeconds(int timeoutSeconds) {
        this.timeoutSeconds = timeoutSeconds;
        return this;
    }

    public CloudShellLauncherOptions withMode(String mode) {
        this.mode = mode;
        return this;
    }

    public CloudShellLauncherOptions withBearerToken(String bearerToken) {
        this.bearerToken = bearerToken;
        return this;
    }

    public CloudShellLauncherOptions withWorkingDirectory(Path workingDirectory) {
        this.workingDirectory = workingDirectory;
        return this;
    }

    public CloudShellLauncherOptions withInheritIo(boolean inheritIo) {
        this.inheritIo = inheritIo;
        return this;
    }
}
