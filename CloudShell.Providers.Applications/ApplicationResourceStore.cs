using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationResourceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _definitionsPath;
    private List<ApplicationResourceDefinition> _definitions;

    public ApplicationResourceStore(
        ApplicationProviderOptions options,
        IHostEnvironment environment)
    {
        _definitionsPath = ResolvePath(options.DefinitionsPath, environment.ContentRootPath);
        _definitions = LoadDefinitions();

        foreach (var application in options.InitialApplications)
        {
            UpsertDefinition(application, persist: true, mergeEnvironment: true);
        }

        foreach (var application in options.DeclaredApplications)
        {
            UpsertDefinition(
                application.Definition,
                persist: false,
                mergeEnvironment: false,
                replaceExisting: !application.Persist || application.OverwritePersistedState);
        }
    }

    public IReadOnlyList<ApplicationResourceDefinition> GetApplications()
    {
        lock (_gate)
        {
            return _definitions
                .OrderBy(application => application.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public ApplicationResourceDefinition? GetApplication(string id)
    {
        lock (_gate)
        {
            return _definitions.FirstOrDefault(application =>
                string.Equals(application.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Save(ApplicationResourceDefinition definition)
    {
        lock (_gate)
        {
            var index = _definitions.FindIndex(application =>
                string.Equals(application.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _definitions[index] = definition;
            }
            else
            {
                _definitions.Add(definition);
            }

            Persist();
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _definitions.RemoveAll(application =>
                string.Equals(application.Id, id, StringComparison.OrdinalIgnoreCase));
            Persist();
        }
    }

    private List<ApplicationResourceDefinition> LoadDefinitions()
    {
        if (!File.Exists(_definitionsPath))
        {
            return [];
        }

        var json = File.ReadAllText(_definitionsPath);
        return JsonSerializer.Deserialize<List<ApplicationResourceDefinition>>(json, SerializerOptions) ?? [];
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_definitionsPath)!);
        File.WriteAllText(_definitionsPath, JsonSerializer.Serialize(_definitions, SerializerOptions));
    }

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, contentRootPath);

    private void UpsertDefinition(
        ApplicationResourceDefinition definition,
        bool persist,
        bool mergeEnvironment,
        bool replaceExisting = true)
    {
        var index = _definitions.FindIndex(item =>
            string.Equals(item.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            if (!replaceExisting)
            {
                return;
            }

            var updated = mergeEnvironment
                ? MergeInitialApplication(_definitions[index], definition)
                : definition;
            if (!Equals(_definitions[index], updated))
            {
                _definitions[index] = updated;
                if (persist)
                {
                    Persist();
                }
            }

            return;
        }

        _definitions.Add(definition);
        if (persist)
        {
            Persist();
        }
    }

    private static ApplicationResourceDefinition MergeInitialApplication(
        ApplicationResourceDefinition existing,
        ApplicationResourceDefinition initial) =>
        existing with
        {
            EnvironmentVariables = existing.EnvironmentVariables
                .Concat(initial.EnvironmentVariables
                    .Where(initialVariable => existing.EnvironmentVariables.All(existingVariable =>
                        !string.Equals(existingVariable.Name, initialVariable.Name, StringComparison.OrdinalIgnoreCase))))
                .ToArray(),
            DependsOn = existing.DependsOn
                .Concat(initial.DependsOn)
                .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
                .Select(dependency => dependency.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
}
