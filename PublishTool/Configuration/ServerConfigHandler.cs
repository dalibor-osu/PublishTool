using Spectre.Console;

namespace PublishTool.Configuration;

public static class ServerConfigHandler
{
    public static IReadOnlyDictionary<string, DeployTarget> ResolveTargets(
        ConfigStore<ServerConfig> store,
        ServerConfig config,
        string prefix,
        IReadOnlyList<string> projects,
        bool interactive,
        Func<string, bool> serviceExists)
    {
        var environment = config.EnvironmentFor(prefix);
        var missing = projects.Where(p => string.IsNullOrWhiteSpace(environment.TargetFor(p)?.Path)).ToList();
        if (missing.Count > 0)
        {
            if (!interactive)
            {
                throw new ConfigException(
                    $"No target directory is set for {string.Join(", ", missing)} in packages of '{prefix}' in {store.Path}. "
                    + "Run deploy once without -y to set it.");
            }

            AnsiConsole.MarkupLineInterpolated(
                $"Packages of [green]{prefix}[/] have no target on this server for: [green]{string.Join(", ", missing)}[/]");
            foreach (string project in missing)
            {
                environment.Targets[project] = AskTarget(prefix, project, serviceExists);
            }

            store.Save(config);
            AnsiConsole.MarkupLineInterpolated($"Saved to [grey]{store.Path}[/]");
            AnsiConsole.WriteLine();
        }

        return projects.ToDictionary(p => p, p => environment.TargetFor(p)!, StringComparer.OrdinalIgnoreCase);
    }

    private static DeployTarget AskTarget(string prefix, string project, Func<string, bool> serviceExists)
    {
        string path = AnsiConsole.Prompt(new TextPrompt<string>(
                $"Enter the [green]target directory[/] of [green]{Markup.Escape(project)}[/] for packages of [green]{Markup.Escape(prefix)}[/]:")
            .Validate(value => Path.IsPathFullyQualified(value.Trim())
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]Enter an absolute path[/]")));
        string service = AnsiConsole.Prompt(new TextPrompt<string>(
                $"Enter the [green]Windows service name[/] of [green]{Markup.Escape(project)}[/] (leave empty for an IIS app):")
            .AllowEmpty()
            .Validate(value => string.IsNullOrWhiteSpace(value) || serviceExists(value.Trim())
                ? ValidationResult.Success()
                : ValidationResult.Error($"[red]The Windows service {Markup.Escape(value.Trim())} does not exist on this machine[/]")));
        return new DeployTarget
        {
            Path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim())),
            ServiceName = string.IsNullOrWhiteSpace(service) ? null : service.Trim()
        };
    }
}
