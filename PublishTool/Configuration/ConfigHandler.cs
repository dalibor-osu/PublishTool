using Spectre.Console;

namespace PublishTool.Configuration;

public static class ConfigHandler
{
    private static readonly string[] DefaultIgnoredDirectories = [".git", ".claude"];

    public static DirectorySettings Load(string workingDir, bool forceUpdate = false)
    {
        var config = ConfigStore.Default.Load();
        var settings = config.For(workingDir);
        if (!settings.IsComplete || forceUpdate)
        {
            RunWizard(workingDir, settings, forceUpdate);
            ConfigStore.Default.Save(config);
        }

        return settings;
    }

    private static void RunWizard(string path, DirectorySettings settings, bool forceUpdate)
    {
        AnsiConsole.Write(new FigletText("PublishTool"));
        AnsiConsole.WriteLine(forceUpdate
            ? $"Editing config for directory ({path})"
            : $"We need to setup some basic configurations for this directory first. ({path})");

        if (settings.PublishDirectory is null || forceUpdate)
        {
            AskPublishDirectory(settings);
        }

        if (settings.SharedDirectory is null || settings.DeployDirectory is null || forceUpdate)
        {
            AskSharedAndDeployDirectories(settings);
        }

        if (settings.IgnoredDirectories is null || forceUpdate)
        {
            AskIgnoredDirectories(settings);
        }

        if (settings.PublishableProjects is null || forceUpdate)
        {
            AskPublishableProjects(path, settings, forceUpdate);
        }
    }

    private static void AskPublishDirectory(DirectorySettings settings)
    {
        settings.PublishDirectory = AnsiConsole.Ask(
            "Enter [green]publish directory[/] path (projects are published into it, one subdirectory per project)",
            settings.PublishDirectory ?? string.Empty);
        AnsiConsole.Clear();
    }

    private static void AskSharedAndDeployDirectories(DirectorySettings settings)
    {
        string sharedDefault = settings.SharedDirectory ?? settings.DeployDirectory ?? string.Empty;
        string deployDefault = settings.SharedDirectory is not null ? settings.DeployDirectory ?? string.Empty : string.Empty;

        settings.SharedDirectory = AnsiConsole.Ask(
            "Enter [green]shared directory[/] path (usually a network share; every complete publish stores its full output there "
            + "as a zip archive that the next one is compared with)",
            sharedDefault);
        settings.DeployDirectory = AnsiConsole.Ask(
            "Enter [green]deploy directory[/] path (local; every complete publish creates a directory there with only the "
            + "files that changed, ready to be deployed)",
            deployDefault);
        AnsiConsole.Clear();
    }

    private static void AskIgnoredDirectories(DirectorySettings settings)
    {
        settings.IgnoredDirectories = AnsiConsole
            .Ask(
                "Enter [green]directories[/] that will be ignored when scanning for projects in this directory (comma separated):",
                string.Join(", ", settings.IgnoredDirectories ?? DefaultIgnoredDirectories))
            .Split(',')
            .Select(s => s.Trim())
            .ToArray();
        AnsiConsole.Clear();
    }

    private static void AskPublishableProjects(string path, DirectorySettings settings, bool forceUpdate)
    {
        var allProjects = ProjectScanner.Scan(settings, path, true);
        if (allProjects.Count == 0)
        {
            throw new ConfigException($"No projects found in {path}.");
        }

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Select [green]projects[/] that will be available to publish from this directory:")
            .AddChoices(allProjects.Select(p => p.Name));
        if (forceUpdate && settings.PublishableProjects is { } preselectedProjects)
        {
            foreach (string preselectedProject in preselectedProjects)
            {
                prompt.Select(preselectedProject);
            }
        }

        string[] selectedProjects = AnsiConsole.Prompt(prompt).ToArray();
        settings.PublishableProjects = allProjects
            .Where(p => selectedProjects.Contains(p.Name))
            .Select(p => p.Name)
            .ToArray();
        AnsiConsole.Clear();
    }
}
