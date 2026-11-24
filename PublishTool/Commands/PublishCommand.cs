using PublishTool.Attributes;
using PublishTool.Commands.Options;
using PublishTool.Configuration;
using PublishTool.Console;
using Spectre.Console;

namespace PublishTool.Commands;

[Command("publish", IsDefault = true, Description = "Publish the projects in a directory")]
public class PublishCommand(PublishCommandOptions options) : ICommand<PublishCommandOptions>, IAlternateScreenCommand
{
    private const int OutputTailLength = 5;

    public PublishCommandOptions Options { get; } = options;

    public bool UsesAlternateScreen => true;

    public async Task<int> ExecuteAsync(CancellationToken ct)
    {
        if (!await PublishWorkflow.IsMsBuildAvailableAsync())
        {
            AnsiConsole.MarkupLine(
                "[red]MSBuild not found! Make sure it is installed and in your PATH environment variable before running this tool.[/]");
            return 1;
        }

        string currentWorkingDirectory = Options.WorkingDirectory;
        var settings = ConfigHandler.Load(currentWorkingDirectory);
        if (!settings.IsComplete)
        {
            throw new ConfigException($"The directory {currentWorkingDirectory} is not fully configured, run 'PublishTool config'.");
        }

        var projects = ProjectScanner.Scan(settings, currentWorkingDirectory);

        if (projects.Count == 0)
        {
            AnsiConsole.MarkupLine("No projects found in this directory.");
            return 1;
        }

        var (projectsToPublish, configuration) = await SelectProjectsAndConfiguration(projects, ct);

        AnsiConsole.Clear();

        var display = new JobDisplay(OutputTailLength);
        var workflow = new PublishWorkflow(
            currentWorkingDirectory,
            projectsToPublish,
            configuration,
            new PublishSettings(settings.PublishDirectory, settings.SharedDirectory, settings.DeployDirectory),
            Options.Local,
            display);
        var work = workflow.RunAsync(ct);
        await JobDisplayRunner.RunAsync(display, work, live: true);

        return (await work).Failed ? 1 : 0;
    }

    private static async Task<(List<DotnetProject> Projects, string Configuration)> SelectProjectsAndConfiguration(
        List<DotnetProject> projects, CancellationToken ct)
    {
        List<string> selectedNames = [];
        while (true)
        {
            var projectPrompt = new MultiSelectionPrompt<string>()
                .Title("Select [green]projects[/] to publish:")
                .AddChoices(projects.Select(p => p.Name));
            foreach (string name in selectedNames)
            {
                projectPrompt.Select(name);
            }

            selectedNames = await AnsiConsole.PromptAsync(projectPrompt, ct);

            if (await PromptConfiguration(selectedNames.Count, ct) is { } configuration)
            {
                var selectedProjects = projects
                    .Where(p => selectedNames.Any(sp => p.AbsolutePath.EndsWith($"{sp}.csproj")))
                    .ToList();
                return (selectedProjects, configuration);
            }
        }
    }

    private static async Task<string?> PromptConfiguration(int selectedCount, CancellationToken ct)
    {
        var prompt = new SelectionPrompt<string>()
            .Title($"Select build Configuration for [green]{selectedCount}[/] selected project(s) [grey](Esc / Left to go back)[/]:")
            .AddChoices(BuildConfigurations.All);
        try
        {
            return await new BackKeyConsole(AnsiConsole.Console).PromptAsync(prompt, ct);
        }
        catch (BackRequestedException)
        {
            AnsiConsole.Clear();
            AnsiConsole.Cursor.Show();
            return null;
        }
    }
}
