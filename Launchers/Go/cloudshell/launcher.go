package cloudshell

import (
	"context"
	"errors"
	"fmt"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

type LauncherOptions struct {
	CLIProject        string
	CloudShellCommand string
	TemplatePath      string
	ControlPlaneURL   string
	StateDir          string
	DataDir           string
	HostProject       string
	HostURL           string
	NoBuild           bool
	TimeoutSeconds    int
	Mode              TemplateApplyMode
	BearerToken       string
	WorkingDirectory  string
	InheritIO         bool
}

type CommandResult struct {
	Command      string
	Args         []string
	ExitCode     int
	TemplatePath string
}

func DefaultLauncherOptions() LauncherOptions {
	return LauncherOptions{
		CloudShellCommand: "cloudshell",
		TimeoutSeconds:    60,
		Mode:              ApplyModeCreateOrUpdate,
		InheritIO:         true,
	}
}

func OptionsFromArgs(args []string) LauncherOptions {
	return OptionsFromArgsWithDefaults(args, DefaultLauncherOptions())
}

func OptionsFromArgsWithDefaults(args []string, defaults LauncherOptions) LauncherOptions {
	options := defaults
	for index := 0; index < len(args); index++ {
		arg := args[index]
		switch arg {
		case "--cli-project":
			options.CLIProject = nextArg(args, &index, arg)
		case "--cloudshell-command":
			options.CloudShellCommand = nextArg(args, &index, arg)
		case "--template-path":
			options.TemplatePath = nextArg(args, &index, arg)
		case "--control-plane":
			options.ControlPlaneURL = nextArg(args, &index, arg)
		case "--state-dir":
			options.StateDir = nextArg(args, &index, arg)
		case "--data-dir":
			options.DataDir = nextArg(args, &index, arg)
		case "--host-project":
			options.HostProject = nextArg(args, &index, arg)
		case "--url":
			options.HostURL = nextArg(args, &index, arg)
		case "--timeout-seconds":
			value := nextArg(args, &index, arg)
			parsed, err := strconv.Atoi(value)
			if err != nil {
				panic(fmt.Sprintf("invalid --timeout-seconds value %q", value))
			}
			options.TimeoutSeconds = parsed
		case "--mode":
			options.Mode = TemplateApplyMode(nextArg(args, &index, arg))
		case "--bearer-token":
			options.BearerToken = nextArg(args, &index, arg)
		case "--cwd":
			options.WorkingDirectory = nextArg(args, &index, arg)
		case "--no-build":
			options.NoBuild = true
		case "--pipe":
			options.InheritIO = false
		default:
			panic(fmt.Sprintf("unknown option: %s", arg))
		}
	}

	return options
}

func (a *App) Apply(options LauncherOptions) (CommandResult, error) {
	options = withDefaults(options)
	templatePath, err := a.writeTemplateForLauncher(options)
	if err != nil {
		return CommandResult{}, err
	}

	command, commandArgs := buildTemplateApplyCommand(templatePath, options, false)
	exitCode, err := runCommand(command, commandArgs, options)
	return CommandResult{
		Command:      command,
		Args:         commandArgs,
		ExitCode:     exitCode,
		TemplatePath: templatePath,
	}, err
}

func (a *App) Start(options LauncherOptions) (CommandResult, error) {
	options = withDefaults(options)
	templatePath, err := a.writeTemplateForLauncher(options)
	if err != nil {
		return CommandResult{}, err
	}

	command, commandArgs := buildTemplateApplyCommand(templatePath, options, true)
	exitCode, err := runCommand(command, commandArgs, options)
	return CommandResult{
		Command:      command,
		Args:         commandArgs,
		ExitCode:     exitCode,
		TemplatePath: templatePath,
	}, err
}

func (a *App) ForegroundRun(options LauncherOptions) (CommandResult, error) {
	options = withDefaults(options)
	templatePath, err := a.writeTemplateForLauncher(options)
	if err != nil {
		return CommandResult{}, err
	}

	hostURL := firstNonBlank(options.HostURL, options.ControlPlaneURL)
	if hostURL == "" {
		return CommandResult{}, errors.New("a host URL or Control Plane URL is required for foreground run")
	}

	hostCommand, hostArgs, err := buildHostRunCommand(options, hostURL)
	if err != nil {
		return CommandResult{}, err
	}

	host, err := startCommand(hostCommand, hostArgs, options)
	if err != nil {
		return CommandResult{}, err
	}
	defer destroyProcess(host)

	if err := waitForReady(host, hostURL, options.BearerToken, options.TimeoutSeconds); err != nil {
		return CommandResult{}, err
	}

	applyOptions := options
	applyOptions.ControlPlaneURL = hostURL
	applyOptions.HostProject = ""
	applyOptions.HostURL = ""
	applyOptions.StateDir = ""
	applyOptions.DataDir = ""
	applyOptions.NoBuild = false

	applyCommand, applyArgs := buildTemplateApplyCommand(templatePath, applyOptions, false)
	applyExitCode, err := runCommand(applyCommand, applyArgs, applyOptions)
	if err != nil || applyExitCode != 0 {
		return CommandResult{
			Command:      applyCommand,
			Args:         applyArgs,
			ExitCode:     applyExitCode,
			TemplatePath: templatePath,
		}, err
	}

	fmt.Println(FormatHostURLMessage(hostURL))
	err = host.Wait()
	return CommandResult{
		Command:      hostCommand,
		Args:         hostArgs,
		ExitCode:     exitCodeFromWaitError(err),
		TemplatePath: templatePath,
	}, nil
}

func (a *App) writeTemplateForLauncher(options LauncherOptions) (string, error) {
	templatePath := options.TemplatePath
	if strings.TrimSpace(templatePath) == "" {
		directory := options.StateDir
		if strings.TrimSpace(directory) == "" {
			temp, err := os.MkdirTemp("", "cloudshell-template-")
			if err != nil {
				return "", err
			}
			directory = temp
		}

		templatePath = filepath.Join(directory, "resources.json")
	}

	return a.WriteTemplate(templatePath)
}

func buildTemplateApplyCommand(templatePath string, options LauncherOptions, startHost bool) (string, []string) {
	args := []string{
		"template",
		"apply",
		templatePath,
	}
	addOption(&args, "--control-plane", options.ControlPlaneURL)
	addOption(&args, "--state-dir", options.StateDir)
	addOption(&args, "--host-project", options.HostProject)
	addOption(&args, "--data-dir", options.DataDir)
	addOption(&args, "--url", options.HostURL)
	if options.TimeoutSeconds > 0 {
		addOption(&args, "--timeout-seconds", strconv.Itoa(options.TimeoutSeconds))
	}
	if options.Mode != "" {
		addOption(&args, "--mode", string(options.Mode))
	}
	addOption(&args, "--bearer-token", options.BearerToken)
	if startHost {
		args = append(args, "--start")
	}

	if options.NoBuild {
		args = append(args, "--no-build")
	}

	if options.CLIProject == "" {
		return options.CloudShellCommand, args
	}

	commandArgs := []string{
		"run",
		"--project",
		options.CLIProject,
		"--",
	}
	commandArgs = append(commandArgs, args...)
	return "dotnet", commandArgs
}

func buildHostRunCommand(options LauncherOptions, hostURL string) (string, []string, error) {
	if strings.TrimSpace(options.HostProject) == "" {
		return "", nil, errors.New("a host project is required for foreground run")
	}

	args := []string{
		"run",
		"--project",
		options.HostProject,
	}
	if options.NoBuild {
		args = append(args, "--no-build")
	}

	args = append(args, "--", "--urls", hostURL)
	if strings.TrimSpace(options.DataDir) != "" {
		args = append(args, "--CloudShell:DataDirectory", options.DataDir)
	}

	return "dotnet", args, nil
}

func FormatHostURLMessage(hostURL string) string {
	return "CloudShell UI: " + strings.TrimRight(hostURL, "/")
}

func runCommand(command string, args []string, options LauncherOptions) (int, error) {
	process, err := startCommand(command, args, options)
	if err != nil {
		return 1, err
	}

	err = process.Wait()
	if err == nil {
		return 0, nil
	}

	var exitError *exec.ExitError
	if errors.As(err, &exitError) {
		return exitError.ExitCode(), nil
	}

	return 1, err
}

func startCommand(command string, args []string, options LauncherOptions) (*exec.Cmd, error) {
	cmd := exec.Command(command, args...)
	if options.WorkingDirectory != "" {
		cmd.Dir = options.WorkingDirectory
	}

	if options.InheritIO {
		cmd.Stdout = os.Stdout
		cmd.Stderr = os.Stderr
		cmd.Stdin = os.Stdin
	}

	return cmd, cmd.Start()
}

func waitForReady(host *exec.Cmd, hostURL string, bearerToken string, timeoutSeconds int) error {
	if timeoutSeconds <= 0 {
		timeoutSeconds = 60
	}

	deadline := time.Now().Add(time.Duration(timeoutSeconds) * time.Second)
	client := http.Client{Timeout: 2 * time.Second}
	url := strings.TrimRight(hostURL, "/") + "/api/control-plane/v1/resources"
	for time.Now().Before(deadline) {
		if host.ProcessState != nil && host.ProcessState.Exited() {
			return errors.New("CloudShell host exited before it was ready")
		}

		request, err := http.NewRequestWithContext(context.Background(), http.MethodGet, url, nil)
		if err != nil {
			return err
		}

		if bearerToken != "" {
			request.Header.Set("Authorization", "Bearer "+bearerToken)
		}

		response, err := client.Do(request)
		if err == nil {
			_ = response.Body.Close()
			if response.StatusCode == http.StatusOK || response.StatusCode == http.StatusNoContent {
				return nil
			}
		}

		time.Sleep(500 * time.Millisecond)
	}

	return fmt.Errorf("CloudShell host did not become ready within %d seconds", timeoutSeconds)
}

func destroyProcess(process *exec.Cmd) {
	if process == nil || process.Process == nil {
		return
	}

	_ = process.Process.Kill()
	_, _ = process.Process.Wait()
}

func exitCodeFromWaitError(err error) int {
	if err == nil {
		return 0
	}

	var exitError *exec.ExitError
	if errors.As(err, &exitError) {
		return exitError.ExitCode()
	}

	return 1
}

func withDefaults(options LauncherOptions) LauncherOptions {
	defaults := DefaultLauncherOptions()
	if options.CloudShellCommand == "" {
		options.CloudShellCommand = defaults.CloudShellCommand
	}
	if options.TimeoutSeconds == 0 {
		options.TimeoutSeconds = defaults.TimeoutSeconds
	}
	if options.Mode == "" {
		options.Mode = defaults.Mode
	}
	return options
}

func addOption(args *[]string, name string, value string) {
	if strings.TrimSpace(value) != "" {
		*args = append(*args, name, strings.TrimSpace(value))
	}
}

func firstNonBlank(values ...string) string {
	for _, value := range values {
		if strings.TrimSpace(value) != "" {
			return strings.TrimSpace(value)
		}
	}

	return ""
}

func nextArg(args []string, index *int, option string) string {
	if *index+1 >= len(args) {
		panic(fmt.Sprintf("%s requires a value", option))
	}

	*index += 1
	return args[*index]
}
