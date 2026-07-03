using CloudShell.Abstractions.ResourceManager;
using ResourceDefinitionApplyMode = CloudShell.ResourceModel.ResourceDefinitionApplyMode;

namespace CloudShell.Cli;

internal static class CommandLineParser
{
    private const string DefaultStateDirectory = ".cloudshell";
    private const string DefaultControlPlaneUrl = "http://127.0.0.1:5097";
    private const int DefaultTimeoutSeconds = 60;

    public static CliCommand Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            return new HelpCommand();
        }

        return args[0] switch
        {
            "control-plane" => ParseControlPlane(args.Skip(1).ToArray()),
            "host" => ParseHost(args.Skip(1).ToArray()),
            "resource" => ParseResource(args.Skip(1).ToArray()),
            "template" => ParseTemplate(args.Skip(1).ToArray()),
            "ui" => ParseUi(args.Skip(1).ToArray()),
            _ => throw new CliUsageException($"Unknown command group '{args[0]}'.")
        };
    }

    private static CliCommand ParseControlPlane(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            return new HelpCommand();
        }

        return args[0] switch
        {
            "start" => ParseControlPlaneStart(args.Skip(1).ToArray()),
            "stop" => ParseControlPlaneStop(args.Skip(1).ToArray()),
            "status" => ParseControlPlaneStatus(args.Skip(1).ToArray()),
            _ => throw new CliUsageException($"Unknown control-plane command '{args[0]}'.")
        };
    }

    private static CliCommand ParseTemplate(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            return new HelpCommand();
        }

        return args[0] switch
        {
            "apply" => ParseTemplateApply(args.Skip(1).ToArray()),
            _ => throw new CliUsageException($"Unknown template command '{args[0]}'.")
        };
    }

    private static CliCommand ParseUi(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            return new HelpCommand();
        }

        return args[0] switch
        {
            "open" => ParseUiOpen(args.Skip(1).ToArray()),
            _ => throw new CliUsageException($"Unknown UI command '{args[0]}'.")
        };
    }

    private static CliCommand ParseResource(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            return new HelpCommand();
        }

        return args[0] switch
        {
            "list" => ParseResourceList(args.Skip(1).ToArray()),
            "show" => ParseResourceShow(args.Skip(1).ToArray()),
            "action" => ParseResourceAction(args.Skip(1).ToArray()),
            _ => throw new CliUsageException($"Unknown resource command '{args[0]}'.")
        };
    }

    private static CliCommand ParseResourceAction(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            return new HelpCommand();
        }

        return args[0] switch
        {
            "execute" => ParseResourceActionExecute(args.Skip(1).ToArray()),
            _ => throw new CliUsageException($"Unknown resource action command '{args[0]}'.")
        };
    }

    private static CliCommand ParseHost(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            return new HelpCommand();
        }

        return args[0] switch
        {
            "names" => ParseHostNames(args.Skip(1).ToArray()),
            _ => throw new CliUsageException($"Unknown host command '{args[0]}'.")
        };
    }

    private static CliCommand ParseHostNames(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            return new HelpCommand();
        }

        return args[0] switch
        {
            "add" => ParseHostNameAdd(args.Skip(1).ToArray()),
            "remove" => ParseHostNameRemove(args.Skip(1).ToArray()),
            _ => throw new CliUsageException($"Unknown host names command '{args[0]}'.")
        };
    }

    private static ControlPlaneStartCommand ParseControlPlaneStart(IReadOnlyList<string> args)
    {
        var options = OptionReader.Read(args);
        options.ThrowIfPositionals();
        var command = new ControlPlaneStartCommand(
            options.ReadString("--state-dir", DefaultStateDirectory),
            options.ReadOptionalString("--host-project"),
            options.ReadOptionalString("--data-dir"),
            options.ReadOptionalString("--host-settings"),
            options.ReadUri("--url", DefaultControlPlaneUrl),
            options.ReadOptionalString("--bearer-token") ??
                Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_TOKEN"),
            options.ReadFlag("--no-build"),
            options.ReadInt("--timeout-seconds", DefaultTimeoutSeconds));
        options.ThrowIfUnreadOptions();
        return command;
    }

    private static ControlPlaneStopCommand ParseControlPlaneStop(IReadOnlyList<string> args)
    {
        var options = OptionReader.Read(args);
        options.ThrowIfPositionals();
        var command = new ControlPlaneStopCommand(
            options.ReadString("--state-dir", DefaultStateDirectory));
        options.ThrowIfUnreadOptions();
        return command;
    }

    private static ControlPlaneStatusCommand ParseControlPlaneStatus(IReadOnlyList<string> args)
    {
        var options = OptionReader.Read(args);
        options.ThrowIfPositionals();
        var command = new ControlPlaneStatusCommand(
            options.ReadString("--state-dir", DefaultStateDirectory),
            options.ReadOptionalString("--bearer-token") ??
                Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_TOKEN"));
        options.ThrowIfUnreadOptions();
        return command;
    }

    private static UiOpenCommand ParseUiOpen(IReadOnlyList<string> args)
    {
        var options = OptionReader.Read(args);
        options.ThrowIfPositionals();
        var command = new UiOpenCommand(
            options.ReadString("--state-dir", DefaultStateDirectory),
            options.ReadOptionalUri("--url"));
        options.ThrowIfUnreadOptions();
        return command;
    }

    private static TemplateApplyCommand ParseTemplateApply(IReadOnlyList<string> args)
    {
        var options = OptionReader.Read(args);
        if (options.Positionals.Count != 1)
        {
            throw new CliUsageException("template apply requires exactly one template file path.");
        }

        var command = new TemplateApplyCommand(
            options.Positionals[0],
            options.ReadString("--state-dir", DefaultStateDirectory),
            options.ReadOptionalUri("--control-plane"),
            options.ReadOptionalString("--bearer-token") ??
                Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_TOKEN"),
            options.ReadFlag("--start"),
            options.ReadOptionalString("--host-project"),
            options.ReadOptionalString("--data-dir"),
            options.ReadOptionalString("--host-settings"),
            options.ReadUri("--url", DefaultControlPlaneUrl),
            options.ReadFlag("--no-build"),
            options.ReadInt("--timeout-seconds", DefaultTimeoutSeconds),
            ParseMode(options.ReadString("--mode", "create-or-update")));
        options.ThrowIfUnreadOptions();
        return command;
    }

    private static ResourceListCommand ParseResourceList(IReadOnlyList<string> args)
    {
        var options = OptionReader.Read(args);
        options.ThrowIfPositionals();
        var command = new ResourceListCommand(
            options.ReadString("--state-dir", DefaultStateDirectory),
            options.ReadOptionalUri("--control-plane"),
            options.ReadOptionalString("--bearer-token") ??
                Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_TOKEN"),
            options.ReadOptionalString("--type"),
            options.ReadOptionalEnum<ResourceClass>("--class"),
            options.ReadOptionalBool("--registered"));
        options.ThrowIfUnreadOptions();
        return command;
    }

    private static ResourceShowCommand ParseResourceShow(IReadOnlyList<string> args)
    {
        var options = OptionReader.Read(args);
        if (options.Positionals.Count != 1)
        {
            throw new CliUsageException("resource show requires exactly one resource id.");
        }

        var command = new ResourceShowCommand(
            options.Positionals[0],
            options.ReadString("--state-dir", DefaultStateDirectory),
            options.ReadOptionalUri("--control-plane"),
            options.ReadOptionalString("--bearer-token") ??
                Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_TOKEN"));
        options.ThrowIfUnreadOptions();
        return command;
    }

    private static ResourceActionExecuteCommand ParseResourceActionExecute(IReadOnlyList<string> args)
    {
        var options = OptionReader.Read(args);
        if (options.Positionals.Count != 2)
        {
            throw new CliUsageException(
                "resource action execute requires a resource id and action id.");
        }

        var command = new ResourceActionExecuteCommand(
            options.Positionals[0],
            options.Positionals[1],
            options.ReadString("--state-dir", DefaultStateDirectory),
            options.ReadOptionalUri("--control-plane"),
            options.ReadOptionalString("--bearer-token") ??
                Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_TOKEN"),
            options.ReadFlag("--start-dependencies"),
            options.ReadFlag("--ignore-dependent-warning"));
        options.ThrowIfUnreadOptions();
        return command;
    }

    private static HostNameAddCommand ParseHostNameAdd(IReadOnlyList<string> args)
    {
        var options = OptionReader.Read(args);
        if (options.Positionals.Count != 2)
        {
            throw new CliUsageException("host names add requires a host name and IP address.");
        }

        var command = new HostNameAddCommand(
            options.Positionals[0],
            options.Positionals[1],
            options.ReadOptionalString("--hosts-file"),
            options.ReadFlag("--dry-run"));
        options.ThrowIfUnreadOptions();
        return command;
    }

    private static HostNameRemoveCommand ParseHostNameRemove(IReadOnlyList<string> args)
    {
        var options = OptionReader.Read(args);
        if (options.Positionals.Count != 1)
        {
            throw new CliUsageException("host names remove requires a host name.");
        }

        var command = new HostNameRemoveCommand(
            options.Positionals[0],
            options.ReadOptionalString("--hosts-file"),
            options.ReadFlag("--dry-run"));
        options.ThrowIfUnreadOptions();
        return command;
    }

    private static ResourceDefinitionApplyMode ParseMode(string value) =>
        value.ToLowerInvariant() switch
        {
            "create-or-update" => ResourceDefinitionApplyMode.CreateOrUpdate,
            "create-only" => ResourceDefinitionApplyMode.CreateOnly,
            "update-existing" => ResourceDefinitionApplyMode.UpdateExisting,
            _ => throw new CliUsageException(
                $"Unsupported apply mode '{value}'. Use create-or-update, create-only, or update-existing.")
        };

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";
}

internal sealed class OptionReader
{
    private readonly Dictionary<string, string?> _options;
    private readonly HashSet<string> _readOptions = new(StringComparer.OrdinalIgnoreCase);

    private OptionReader(
        Dictionary<string, string?> options,
        IReadOnlyList<string> positionals)
    {
        _options = options;
        Positionals = positionals;
    }

    public IReadOnlyList<string> Positionals { get; }

    public static OptionReader Read(IReadOnlyList<string> args)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(arg);
                continue;
            }

            if (IsFlag(arg))
            {
                options[arg] = null;
                continue;
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new CliUsageException($"Option '{arg}' requires a value.");
            }

            options[arg] = args[++i];
        }

        return new OptionReader(options, positionals);
    }

    public string ReadString(string name, string defaultValue)
    {
        _readOptions.Add(name);
        return _options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    public string? ReadOptionalString(string name)
    {
        _readOptions.Add(name);
        return _options.TryGetValue(name, out var value)
            ? value
            : null;
    }

    public Uri ReadUri(string name, string defaultValue) =>
        ReadUriValue(name, ReadString(name, defaultValue));

    public Uri? ReadOptionalUri(string name)
    {
        var value = ReadOptionalString(name);
        return value is null
            ? null
            : ReadUriValue(name, value);
    }

    public int ReadInt(string name, int defaultValue)
    {
        var value = ReadString(name, defaultValue.ToString());
        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            throw new CliUsageException($"Option '{name}' must be a positive integer.");
        }

        return parsed;
    }

    public bool ReadFlag(string name)
    {
        _readOptions.Add(name);
        return _options.ContainsKey(name);
    }

    public bool? ReadOptionalBool(string name)
    {
        var value = ReadOptionalString(name);
        if (value is null)
        {
            return null;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw new CliUsageException($"Option '{name}' must be true or false.");
        }

        return parsed;
    }

    public TEnum? ReadOptionalEnum<TEnum>(string name)
        where TEnum : struct
    {
        var value = ReadOptionalString(name);
        if (value is null)
        {
            return null;
        }

        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            throw new CliUsageException($"Option '{name}' has unsupported value '{value}'.");
        }

        return parsed;
    }

    public void ThrowIfPositionals()
    {
        if (Positionals.Count != 0)
        {
            throw new CliUsageException($"Unexpected argument '{Positionals[0]}'.");
        }
    }

    public void ThrowIfUnreadOptions()
    {
        var unread = _options.Keys
            .Where(option => !_readOptions.Contains(option))
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (unread is not null)
        {
            throw new CliUsageException($"Unknown option '{unread}'.");
        }
    }

    private static bool IsFlag(string value) =>
        value is "--start" or "--no-build" or "--start-dependencies" or "--ignore-dependent-warning" or "--dry-run";

    private static Uri ReadUriValue(string name, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new CliUsageException($"Option '{name}' must be an absolute HTTP or HTTPS URL.");
        }

        return uri;
    }
}

internal sealed class CliUsageException(string message) : Exception(message);
