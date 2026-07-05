package com.cloudshell.sdk;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.OffsetDateTime;
import java.time.format.DateTimeParseException;
import java.util.Map;

public final class ProfileTokenCredential implements CloudShellTokenCredential {
    public static final String CONFIG_DIRECTORY_ENVIRONMENT_VARIABLE = "CLOUDSHELL_CONFIG_DIR";
    public static final String PROFILE_ENVIRONMENT_VARIABLE = "CLOUDSHELL_PROFILE";
    public static final String DEFAULT_CONFIG_DIRECTORY_NAME = ".cloudshell";
    public static final String DEFAULT_CONFIG_FILE_NAME = "config.json";

    private final Path configDirectory;
    private final Path configPath;
    private final String profileName;
    private final Map<String, String> environment;

    public ProfileTokenCredential() {
        this(null, null, null, System.getenv());
    }

    public ProfileTokenCredential(
        Path configDirectory,
        Path configPath,
        String profileName,
        Map<String, String> environment) {
        this.configDirectory = configDirectory;
        this.configPath = configPath;
        this.profileName = profileName;
        this.environment = environment == null ? Map.of() : environment;
    }

    @Override
    public String getToken(String[] scopes) {
        Path resolvedConfigPath = resolveConfigPath();
        if (!Files.exists(resolvedConfigPath)) {
            return null;
        }

        String json;
        try {
            json = Files.readString(resolvedConfigPath);
        } catch (IOException exception) {
            throw new IllegalStateException(
                "CloudShell profile credential could not read '" + resolvedConfigPath + "'.",
                exception);
        }

        String selectedProfileName = firstNotBlank(
            profileName,
            environment.get(PROFILE_ENVIRONMENT_VARIABLE),
            CloudShellJson.stringProperty(json, "activeProfile"));
        if (selectedProfileName == null) {
            return null;
        }

        String profiles = CloudShellJson.objectProperty(json, "profiles");
        if (profiles == null) {
            return null;
        }

        String profile = CloudShellJson.objectPropertyIgnoreCase(profiles, selectedProfileName);
        if (profile == null) {
            return null;
        }

        String credential = CloudShellJson.objectProperty(profile, "credential");
        if (credential == null) {
            return null;
        }

        String kind = CloudShellJson.stringProperty(credential, "kind");
        if (kind == null || !kind.equalsIgnoreCase("staticBearer")) {
            return null;
        }

        String expiresOn = CloudShellJson.stringProperty(credential, "expiresOn");
        if (isExpired(expiresOn)) {
            return null;
        }

        String token = firstNotBlank(CloudShellJson.stringProperty(credential, "accessToken"));
        if (token == null) {
            token = readTokenFile(
                resolvedConfigPath,
                CloudShellJson.stringProperty(credential, "accessTokenPath"));
        }

        return token == null ? null : token.trim();
    }

    private Path resolveConfigPath() {
        if (configPath != null) {
            return configPath;
        }

        return resolveConfigDirectory().resolve(DEFAULT_CONFIG_FILE_NAME);
    }

    private Path resolveConfigDirectory() {
        if (configDirectory != null) {
            return configDirectory;
        }

        String configuredDirectory = environment.get(CONFIG_DIRECTORY_ENVIRONMENT_VARIABLE);
        if (configuredDirectory != null && !configuredDirectory.isBlank()) {
            return Path.of(configuredDirectory.trim());
        }

        String homeDirectory = System.getProperty("user.home", "");
        return homeDirectory.isBlank()
            ? Path.of(DEFAULT_CONFIG_DIRECTORY_NAME)
            : Path.of(homeDirectory, DEFAULT_CONFIG_DIRECTORY_NAME);
    }

    private static String readTokenFile(Path configPath, String accessTokenPath) {
        if (accessTokenPath == null || accessTokenPath.isBlank()) {
            return null;
        }

        Path tokenPath = Path.of(accessTokenPath.trim());
        if (!tokenPath.isAbsolute()) {
            Path parent = configPath.getParent();
            tokenPath = parent == null ? tokenPath : parent.resolve(tokenPath);
        }

        if (!Files.exists(tokenPath)) {
            return null;
        }

        try {
            return Files.readString(tokenPath);
        } catch (IOException exception) {
            throw new IllegalStateException(
                "CloudShell profile credential could not read token file '" + tokenPath + "'.",
                exception);
        }
    }

    private static boolean isExpired(String expiresOn) {
        if (expiresOn == null || expiresOn.isBlank()) {
            return false;
        }

        try {
            return OffsetDateTime.parse(expiresOn).toInstant().toEpochMilli() <=
                System.currentTimeMillis();
        } catch (DateTimeParseException exception) {
            throw new IllegalStateException(
                "CloudShell profile credential has an invalid expiresOn value '" + expiresOn + "'.",
                exception);
        }
    }

    private static String firstNotBlank(String... values) {
        for (String value : values) {
            if (value != null && !value.isBlank()) {
                return value.trim();
            }
        }

        return null;
    }
}
