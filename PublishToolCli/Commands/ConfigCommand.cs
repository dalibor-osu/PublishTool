using PublishTool;
using PublishTool.Attributes;
using PublishTool.Commands;
using PublishTool.Configuration;
using PublishTool.Deployment;
using PublishToolCli.Commands.Options;
using PublishToolCli.Output;

namespace PublishToolCli.Commands;

[Command("config", Description = "Show or change the stored settings of a directory, or with --prefix the server deploy targets of a package prefix")]
public class ConfigCommand(ConfigCommandOptions options) : ICommand<ConfigCommandOptions>
{
    private const string NotSet = "(not set)";

    public ConfigCommandOptions Options { get; } = options;

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        var output = new CliOutput(Options.Json, false);
        var result = new ConfigCliResult();
        return output.RunAsync(result, CliJsonContext.Default.ConfigCliResult, () => Task.FromResult(Run(output, result)), ct);
    }

    private int Run(CliOutput output, ConfigCliResult result)
    {
        if (Options.Prefix is not null)
        {
            if (Options.ChangesDirectory)
            {
                throw new CliArgumentException("Directory settings cannot be changed together with --prefix; run config twice");
            }

            return RunTargets(output, result, Options.Prefix.Trim());
        }

        if (Options.ChangesTargets)
        {
            throw new CliArgumentException(
                "--target, --service, --remove-target, --skip, --remove-skip, --keep and --remove-keep need --prefix <package prefix>");
        }

        return RunDirectory(output, result);
    }

    private int RunDirectory(CliOutput output, ConfigCliResult result)
    {
        var store = ConfigStore.Default;
        var config = store.Load();
        string directory = Options.WorkingDirectory;
        result.ConfigPath = store.Path;
        result.Directory = directory;
        if (Options.ChangesDirectory)
        {
            ApplyDirectoryChanges(config.For(directory), directory);
            store.Save(config);
            result.Changed = true;
        }

        var settings = config.Directories.TryGetValue(directory, out var found) ? found : null;
        result.Settings = settings;
        result.MissingSettings = [.. (settings ?? new DirectorySettings()).MissingFor(local: false, projectsGiven: false)];

        output.Line($"{(result.Changed ? "Saved settings" : "Settings")} of {directory} ({store.Path}):");
        output.Line($"  {SettingKeys.PublishDirectory}: {settings?.PublishDirectory ?? NotSet}");
        output.Line($"  {SettingKeys.SharedDirectory}: {settings?.SharedDirectory ?? NotSet}");
        output.Line($"  {SettingKeys.DeployDirectory}: {settings?.DeployDirectory ?? NotSet}");
        output.Line($"  {SettingKeys.IgnoredDirectories}: {(settings?.IgnoredDirectories is { } ignored
            ? string.Join(", ", ignored)
            : $"{NotSet}, {string.Join(", ", DirectorySettings.DefaultIgnoredDirectories)} is used")}");
        output.Line($"  {SettingKeys.PublishableProjects}: {(settings?.PublishableProjects is { } publishable ? string.Join(", ", publishable) : NotSet)}");
        return ExitCodes.Success;
    }

    private void ApplyDirectoryChanges(DirectorySettings settings, string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new CliArgumentException($"The directory {directory} does not exist");
        }

        foreach (string key in Options.Unset)
        {
            Unset(settings, key);
        }

        settings.PublishDirectory = Options.PublishDirectory ?? settings.PublishDirectory;
        settings.SharedDirectory = Options.SharedDirectory ?? settings.SharedDirectory;
        settings.DeployDirectory = Options.DeployDirectory ?? settings.DeployDirectory;
        settings.IgnoredDirectories = Options.IgnoredDirectories ?? settings.IgnoredDirectories;
        if (Options.PublishableProjects is { } names)
        {
            settings.PublishableProjects = ValidatePublishable(settings, directory, names);
        }
    }

    private static void Unset(DirectorySettings settings, string key)
    {
        switch (key)
        {
            case SettingKeys.PublishDirectory:
                settings.PublishDirectory = null;
                break;
            case SettingKeys.SharedDirectory:
                settings.SharedDirectory = null;
                break;
            case SettingKeys.DeployDirectory:
                settings.DeployDirectory = null;
                break;
            case SettingKeys.IgnoredDirectories:
                settings.IgnoredDirectories = null;
                break;
            default:
                settings.PublishableProjects = null;
                break;
        }
    }

    private static string[] ValidatePublishable(DirectorySettings settings, string directory, string[] names)
    {
        var scanSettings = new DirectorySettings { IgnoredDirectories = settings.IgnoredDirectories ?? [.. DirectorySettings.DefaultIgnoredDirectories] };
        var problems = new List<string>();
        var selected = ProjectScanner.SelectByName(ProjectScanner.Scan(scanSettings, directory, true), names, problems);
        if (problems.Count > 0)
        {
            throw new CliArgumentException(string.Join("; ", problems));
        }

        return [.. selected.Select(p => p.Name)];
    }

    private int RunTargets(CliOutput output, ConfigCliResult result, string prefix)
    {
        var store = ConfigStore.Server;
        var config = store.Load();
        result.ConfigPath = store.Path;
        result.Prefix = prefix;
        if (Options.ChangesTargets)
        {
            ApplyTargetChanges(config.EnvironmentFor(prefix));
            try
            {
                store.Save(config);
            }
            catch (UnauthorizedAccessException e)
            {
                Logger.LogError(e.ToString());
                return output.Fail(result, ExitCodes.Failed, $"{store.Path} cannot be written, run config from an elevated terminal");
            }

            result.Changed = true;
        }

        var environment = config.FindEnvironment(prefix);
        var targets = environment?.Targets ?? [];
        result.Targets = targets;
        result.Skipped = [.. environment?.Skipped ?? []];
        output.Line($"{(result.Changed ? "Saved deploy targets" : "Deploy targets")} of packages with prefix {prefix} ({store.Path}):");
        if (targets.Count == 0 && result.Skipped.Count == 0)
        {
            output.Line("  (none)");
        }

        foreach (var (project, target) in targets.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
        {
            output.Line($"  {project}: {target.Path ?? NotSet}, {(target.ServiceName is null ? "IIS" : $"service {target.ServiceName}")}"
                + (target.Keep.Count == 0 ? string.Empty : $", clean keeps {string.Join(", ", target.Keep)}"));
        }

        foreach (string project in result.Skipped.Order(StringComparer.OrdinalIgnoreCase))
        {
            output.Line($"  {project}: not deployed on this server");
        }

        return ExitCodes.Success;
    }

    private void ApplyTargetChanges(DeployEnvironment environment)
    {
        CheckSkipConflicts(environment);
        var removed = Options.RemoveTargets
            .Select(project => ServerConfig.FindKey(environment.Targets, project)
                ?? throw new CliArgumentException($"{project} has no target to remove"))
            .ToList();
        var updated = Options.Targets.Select(change => (Key: ServerConfig.FindKey(environment.Targets, change.Key) ?? change.Key,
                Target: Merge(environment.TargetFor(change.Key), change.Key, change.Value)))
            .ToList();

        foreach (string key in removed)
        {
            environment.Targets.Remove(key);
        }

        foreach (string project in Options.RemoveSkip)
        {
            environment.Unskip(project);
        }

        foreach (string project in Options.Skip)
        {
            environment.Skip(project);
        }

        foreach (var (key, target) in updated)
        {
            environment.Unskip(key);
            environment.Targets[key] = target;
        }

        foreach (var (project, pattern) in Options.RemoveKeep)
        {
            var target = environment.TargetFor(project) ?? throw new CliArgumentException($"{project} has no target");
            if (!target.RemoveKeep(pattern))
            {
                throw new CliArgumentException($"{project} has no keep pattern '{pattern}'");
            }
        }
    }

    private void CheckSkipConflicts(DeployEnvironment environment)
    {
        if (Options.Skip.FirstOrDefault(p => Options.Targets.ContainsKey(p) || Options.RemoveSkip.Contains(p, StringComparer.OrdinalIgnoreCase))
            is { } conflict)
        {
            throw new CliArgumentException($"{conflict} cannot be skipped and given a target or un-skipped in the same run");
        }

        if (Options.RemoveSkip.FirstOrDefault(p => !environment.IsSkipped(p)) is { } notSkipped)
        {
            throw new CliArgumentException($"{notSkipped} is not skipped");
        }
    }

    internal static DeployTarget Merge(DeployTarget? stored, string project, TargetOverride change)
    {
        string? path = change.Path ?? stored?.Path;
        if (path is null)
        {
            throw new CliArgumentException($"{project} has no target yet, set it with --target {project}=<absolute path>");
        }

        string? service = change.ServiceSet ? change.ServiceName : stored?.ServiceName;
        if (service is not null && OperatingSystem.IsWindows() && !new WindowsHostController().ServiceExists(service))
        {
            throw new CliArgumentException($"The Windows service {service} does not exist on this machine");
        }

        return new DeployTarget
        {
            Path = path,
            ServiceName = service,
            Keep = [.. (stored?.Keep ?? []).Union(change.Keep, StringComparer.OrdinalIgnoreCase)]
        };
    }
}
