using System.Text.Json;
using Spectre.Console;

namespace PublishTool;

public static class ConfigHandler {
  public static Config Instance => _instance ?? throw new InvalidOperationException("Config not loaded!");
  private static string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PublishTool");

  private static Config? _instance = null;

  public static void Load(string workingDir) {
    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PublishTool", "config.json");
    if (File.Exists(path)) {
      string json = File.ReadAllText(path);
      _instance = JsonSerializer.Deserialize<Config>(json, JsonContext.Default.Config) ??
                  throw new InvalidOperationException("Failed to deserialize config file!");
    } else {
      _instance = new Config();
    }

    HandleDirConfig(workingDir);
  }

  private static void HandleDirConfig(string path) {
    bool needsUpdate = !Instance.PublishDirectories.ContainsKey(path) || !Instance.IgnoredDirectories.ContainsKey(path) || !Instance.PublishableProjects.ContainsKey(path);
    if (needsUpdate) {
      AnsiConsole.Write(new FigletText("PublishTool"));
      AnsiConsole.WriteLine($"We need to setup some basic configurations for this directory first. ({path})");
    }

    if (!Instance.PublishDirectories.ContainsKey(path)) {
      string publishPath = AnsiConsole.Ask<string>("Enter publish directory path");
      Instance.PublishDirectories[path] = publishPath;
      needsUpdate = true;
    }

    if (!Instance.IgnoredDirectories.ContainsKey(path)) {
      string[] ignoredDirs = AnsiConsole
        .Ask<string>("Enter [green]directories[/] that will be ignored when scanning for projects in this directory (comma separated):").Split(',')
        .Select(s => s.Trim()).ToArray();
      Instance.IgnoredDirectories[path] = ignoredDirs;
      needsUpdate = true;
    }

    if (!Instance.PublishableProjects.ContainsKey(path)) {
      var allProjects = ProjectScanner.Scan(path, true);
      if (allProjects.Count == 0) {
        AnsiConsole.MarkupLine("No projects found in this directory.");
        Environment.Exit(1);
        return;
      }

      string[] selectedProjects = AnsiConsole.Prompt(
        new MultiSelectionPrompt<string>()
          .Title("Select [green]projects[/] that will be available to publish from this directory:")
          .AddChoices(allProjects.Select(p => p.Name))).ToArray();

      var finalProjects = allProjects.Where(p => selectedProjects.Contains(p.Name)).ToArray();
      Instance.PublishableProjects[path] = finalProjects.Select(p => p.Name).ToArray();
      needsUpdate = true;
    }


    if (needsUpdate) {
      Save();
    }

    AnsiConsole.Clear();
  }

  private static void Save() {
    if (!Directory.Exists(ConfigDir)) {
      Directory.CreateDirectory(ConfigDir);
    }

    string path = Path.Combine(ConfigDir, "config.json");
    string json = JsonSerializer.Serialize(_instance, JsonContext.Default.Config);
    File.WriteAllText(path, json);
  }
}