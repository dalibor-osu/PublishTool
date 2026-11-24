using Spectre.Console;

namespace PublishTool.Configuration;

public static class ServerConfigHandler
{
    public static IReadOnlyDictionary<string, DeployTarget> ResolveTargets(
        ConfigStore<ServerConfig> store,
        ServerConfig config,
        string prefix,
        IReadOnlyList<string> projects,
        Func<string, bool> serviceExists)
    {
        var resolution = DeployTargets.Resolve(config, prefix, projects);
        if (resolution.Missing.Count == 0)
        {
            return resolution.Targets;
        }

        AnsiConsole.MarkupLineInterpolated(
            $"Packages of [green]{prefix}[/] have no target on this server for: [green]{string.Join(", ", resolution.Missing)}[/]");
        var environment = config.EnvironmentFor(prefix);
        foreach (string project in resolution.Missing)
        {
            environment.Targets[project] = AskTarget(prefix, project, serviceExists);
        }

        store.Save(config);
        AnsiConsole.MarkupLineInterpolated($"Saved to [grey]{store.Path}[/]");
        AnsiConsole.WriteLine();
        return DeployTargets.Resolve(config, prefix, projects).Targets;
    }

    private static DeployTarget AskTarget(string prefix, string project, Func<string, bool> serviceExists)
    {
        string path = AnsiConsole.Prompt(new TextPrompt<string>(
                $"Enter the [green]target directory[/] of [green]{Markup.Escape(project)}[/] for packages of [green]{Markup.Escape(prefix)}[/]:")
            .Validate(value => DeployTargets.TryNormalizePath(value, out _)
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]Enter an absolute path[/]")));
        string service = AnsiConsole.Prompt(new TextPrompt<string>(
                $"Enter the [green]Windows service name[/] of [green]{Markup.Escape(project)}[/] (leave empty for an IIS app):")
            .AllowEmpty()
            .Validate(value => string.IsNullOrWhiteSpace(value) || serviceExists(value.Trim())
                ? ValidationResult.Success()
                : ValidationResult.Error($"[red]The Windows service {Markup.Escape(value.Trim())} does not exist on this machine[/]")));
        DeployTargets.TryNormalizePath(path, out string normalized);
        return new DeployTarget
        {
            Path = normalized,
            ServiceName = string.IsNullOrWhiteSpace(service) ? null : service.Trim()
        };
    }
}
