using System.Text.Json;
using Spectre.Console;

namespace PublishTool;

public static class ConfigHandler
{
    private static string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PublishTool");

    public static Config Load(string workingDir, bool forceUpdate = false)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PublishTool", "config.json");
        var result = new Config();
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            result = JsonSerializer.Deserialize<Config>(json, JsonContext.Default.Config) ??
                     throw new InvalidOperationException("Failed to deserialize config file!");
        }

        HandleDirConfig(workingDir, result, forceUpdate);
        return result;
    }

    private static void HandleDirConfig(string path, Config config, bool forceUpdate)
    {
        bool needsUpdate = !config.PublishDirectories.ContainsKey(path) || !config.SharedDirectories.ContainsKey(path) ||
                           !config.DeployDirectories.ContainsKey(path) || !config.IgnoredDirectories.ContainsKey(path) ||
                           !config.PublishableProjects.ContainsKey(path) || forceUpdate;
        if (needsUpdate)
        {
            AnsiConsole.Write(new FigletText("PublishTool"));
            AnsiConsole.WriteLine(forceUpdate
                ? $"Editing config for directory ({path})"
                : $"We need to setup some basic configurations for this directory first. ({path})");
        }

        if (!config.PublishDirectories.ContainsKey(path) || forceUpdate)
        {
            string publishPath = AnsiConsole.Ask(
                "Enter [green]publish directory[/] path (projects are published into it, one subdirectory per project)",
                config.PublishDirectories.GetValueOrDefault(path, string.Empty));
            config.PublishDirectories[path] = publishPath;
            needsUpdate = true;
            AnsiConsole.Clear();
        }

        if (!config.SharedDirectories.ContainsKey(path) || !config.DeployDirectories.ContainsKey(path) || forceUpdate)
        {
            bool hadSharedDirectory = config.SharedDirectories.ContainsKey(path);
            string sharedDefault = config.SharedDirectories.GetValueOrDefault(path)
                                   ?? config.DeployDirectories.GetValueOrDefault(path, string.Empty);
            string deployDefault = hadSharedDirectory ? config.DeployDirectories.GetValueOrDefault(path, string.Empty) : string.Empty;

            config.SharedDirectories[path] = AnsiConsole.Ask(
                "Enter [green]shared directory[/] path (usually a network share; every complete publish stores its full output "
                + "there as the reference the next one is compared with)",
                sharedDefault);
            config.DeployDirectories[path] = AnsiConsole.Ask(
                "Enter [green]deploy directory[/] path (local; every complete publish creates a directory there with only the "
                + "files that changed, ready to be deployed)",
                deployDefault);
            needsUpdate = true;
            AnsiConsole.Clear();
        }

        if (!config.IgnoredDirectories.ContainsKey(path) || forceUpdate)
        {
            string[] ignoredDirs = AnsiConsole
                .Ask(
                    "Enter [green]directories[/] that will be ignored when scanning for projects in this directory (comma separated):",
                    string.Join(", ", config.IgnoredDirectories.GetValueOrDefault(path, [".git, .claude"])))
                .Split(',')
                .Select(s => s.Trim()).ToArray();
            config.IgnoredDirectories[path] = ignoredDirs;
            needsUpdate = true;
            AnsiConsole.Clear();
        }

        if (!config.PublishableProjects.ContainsKey(path) || forceUpdate)
        {
            var allProjects = ProjectScanner.Scan(config, path, true);
            if (allProjects.Count == 0)
            {
                AnsiConsole.MarkupLine("No projects found in this directory.");
                Environment.Exit(1);
                return;
            }

            var prompt = new MultiSelectionPrompt<string>()
                .Title("Select [green]projects[/] that will be available to publish from this directory:")
                .AddChoices(allProjects.Select(p => p.Name));
            if (forceUpdate && config.PublishableProjects.TryGetValue(path, out string[]? preselectedProjects))
            {
                foreach (string preselectedProject in preselectedProjects)
                {
                    prompt.Select(preselectedProject);
                }
            }

            string[] selectedProjects = AnsiConsole.Prompt(prompt).ToArray();

            var finalProjects = allProjects.Where(p => selectedProjects.Contains(p.Name)).ToArray();
            config.PublishableProjects[path] = finalProjects.Select(p => p.Name).ToArray();
            needsUpdate = true;
            AnsiConsole.Clear();
        }

        if (needsUpdate)
        {
            Save(config);
        }
    }

    private static void Save(Config config)
    {
        if (!Directory.Exists(ConfigDir))
        {
            Directory.CreateDirectory(ConfigDir);
        }

        string path = Path.Combine(ConfigDir, "config.json");
        string json = JsonSerializer.Serialize(config, JsonContext.Default.Config);
        File.WriteAllText(path, json);
    }
}
