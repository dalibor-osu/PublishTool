using PublishTool;
using PublishTool.Attributes;
using PublishTool.Commands;
using PublishTool.Configuration;
using PublishToolCli.Commands.Options;
using PublishToolCli.Output;

namespace PublishToolCli.Commands;

[Command("publish", Description = "Build and publish projects, then prepare the deploy package of the current git branch (unless --local)")]
public class PublishCommand(PublishCommandOptions options) : ICommand<PublishCommandOptions>
{
    public PublishCommandOptions Options { get; } = options;

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        var output = new CliOutput(Options.Json, Options.Verbose);
        var result = new PublishCliResult
        {
            WorkingDirectory = Options.WorkingDirectory,
            Configuration = Options.Configuration,
            Local = Options.Local
        };
        return output.RunAsync(result, CliJsonContext.Default.PublishCliResult, () => RunAsync(output, result, ct), ct);
    }

    private async Task<int> RunAsync(CliOutput output, PublishCliResult result, CancellationToken ct)
    {
        if (!Directory.Exists(Options.WorkingDirectory))
        {
            throw new CliArgumentException($"The directory {Options.WorkingDirectory} does not exist");
        }

        var settings = ResolveSettings();
        var projects = FindProjects(settings);
        if (projects.Count == 0)
        {
            return output.Fail(result, ExitCodes.Failed, $"No publishable projects were found in {Options.WorkingDirectory}");
        }

        result.Projects = [.. projects.Select(p => p.Name)];
        if (!await PublishWorkflow.IsMsBuildAvailableAsync())
        {
            return output.Fail(result, ExitCodes.Failed, "MSBuild was not found; install it and put it on the PATH environment variable");
        }

        output.Line($"Publishing {string.Join(", ", result.Projects)} ({Options.Configuration}{(Options.Local ? ", local only" : string.Empty)})");
        var steps = new LineSteps(output);
        var workflow = new PublishWorkflow(
            Options.WorkingDirectory,
            projects,
            Options.Configuration,
            new PublishSettings(settings.PublishDirectory!, settings.SharedDirectory, settings.DeployDirectory),
            Options.Local,
            steps);
        var published = await workflow.RunAsync(ct);
        result.Steps = steps.ToResult();
        Fill(result, published);
        if (published.Cancelled)
        {
            return ExitCodes.Cancelled;
        }

        if (published.Failed)
        {
            return output.Fail(result, ExitCodes.Failed, "The publish failed, see the failed step above");
        }

        output.Line(published.PackageDirectory is null
            ? $"Published into {settings.PublishDirectory}"
            : $"Package {published.PackageName} is ready in {published.PackageDirectory}");
        return ExitCodes.Success;
    }

    private DirectorySettings ResolveSettings()
    {
        var config = ConfigStore.Default.Load();
        var stored = config.Directories.TryGetValue(Options.WorkingDirectory, out var found) ? found : new DirectorySettings();
        var settings = stored.With(new SettingsOverrides
        {
            PublishDirectory = Options.PublishDirectory,
            SharedDirectory = Options.SharedDirectory,
            DeployDirectory = Options.DeployDirectory,
            IgnoredDirectories = Options.IgnoredDirectories
        });
        settings.IgnoredDirectories ??= [.. DirectorySettings.DefaultIgnoredDirectories];

        var missing = settings.MissingFor(Options.Local, Options.Projects.Count > 0);
        if (missing.Count > 0)
        {
            throw new CliArgumentException(
                $"Missing settings for {Options.WorkingDirectory}: {string.Join(", ", missing)}. Pass them as --<setting> options for this run, "
                + $"or store them with 'PublishToolCli config -dir \"{Options.WorkingDirectory}\" --<setting> <value>'"
                + (missing.Contains(SettingKeys.PublishableProjects) ? ", or name the projects with --projects" : string.Empty));
        }

        return settings;
    }

    private List<DotnetProject> FindProjects(DirectorySettings settings)
    {
        if (Options.Projects.Count == 0)
        {
            return ProjectScanner.Scan(settings, Options.WorkingDirectory);
        }

        var problems = new List<string>();
        var selected = ProjectScanner.SelectByName(ProjectScanner.Scan(settings, Options.WorkingDirectory, true), Options.Projects, problems);
        if (problems.Count > 0)
        {
            throw new CliArgumentException(string.Join("; ", problems));
        }

        return selected;
    }

    private static void Fill(PublishCliResult result, PublishResult published)
    {
        result.PackageName = published.PackageName;
        result.PackageDirectory = published.PackageDirectory;
        result.Reference = published.Reference;
        result.PreviousReference = published.PreviousReference;
        result.Changes = published.Projects;
        result.Carried = published.Carried;
        result.Pruned = published.Pruned;
    }
}
