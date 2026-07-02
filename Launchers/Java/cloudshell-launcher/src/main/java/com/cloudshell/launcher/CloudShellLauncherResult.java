package com.cloudshell.launcher;

import java.nio.file.Path;
import java.util.List;

public record CloudShellLauncherResult(
    String command,
    List<String> arguments,
    int exitCode,
    Path templatePath) {
}
