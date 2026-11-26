using PublishTool.Configuration;
using PublishTool.Deployment;
using Spectre.Console;

namespace PublishTool.Console;

internal static class CleanReview
{
    private const string ReportFileName = "publishtool-clean.txt";
    private const string Accept = "Accept and continue";
    private const string AddKeep = "Keep some of them (add a keep pattern for a target)";
    private const string WithoutCleaning = "Continue without cleaning";
    private const string Cancel = "Cancel, change nothing";

    public static async Task<bool> RunAsync(DeployPlan plan, ConfigStore<ServerConfig> store, ServerConfig config, CancellationToken ct)
    {
        var question = new ConfirmationPrompt("Clean up the target directories (delete files that are not part of the publish)?")
        {
            DefaultValue = false
        };
        if (!await AnsiConsole.PromptAsync(question, ct))
        {
            return true;
        }

        var inventory = plan.Package.LoadInventory();
        string choice = AddKeep;
        while (choice == AddKeep)
        {
            AnsiConsole.MarkupLine("Comparing the targets with the publish...");
            plan.Clean(inventory, null, null, ct);
            var warnings = plan.Warnings.Where(w => WarningCodes.IsClean(w.Code)).ToList();
            WriteFindings(plan, warnings);
            if (plan.Errors.Count > 0)
            {
                AnsiConsole.MarkupLine("Nothing was changed.");
                return false;
            }

            if (warnings.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]The targets hold nothing to clean.[/]");
                return true;
            }

            choice = await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>().Title("What should happen with these files?").AddChoices(Accept, AddKeep, WithoutCleaning, Cancel), ct);
            if (choice == AddKeep)
            {
                await AskKeepAsync(plan, inventory, store, config, ct);
            }
        }

        if (choice == WithoutCleaning)
        {
            plan.ResetClean();
        }

        if (choice == Cancel)
        {
            AnsiConsole.MarkupLine("Nothing was changed.");
        }

        return choice != Cancel;
    }

    private static void WriteFindings(DeployPlan plan, List<DeployWarning> warnings)
    {
        var cleaned = plan.Targets.Where(t => t.Stray.Count + t.EmptyDirectories.Count > 0).ToList();
        if (cleaned.Count > 0)
        {
            var table = new Table().AddColumns("Project", "Target", "Delete files", "Remove empty directories");
            foreach (var target in cleaned)
            {
                table.AddRow(
                    Markup.Escape(target.Project.Name),
                    Markup.Escape(target.TargetPath),
                    target.Stray.Count.ToString(),
                    target.EmptyDirectories.Count.ToString());
            }

            AnsiConsole.Write(table);
        }

        if (plan.NotCleaned.Count > 0)
        {
            AnsiConsole.MarkupLineInterpolated($"Not cleaned, no target on this server: [grey]{string.Join(", ", plan.NotCleaned)}[/]");
        }

        if (plan.ProtectedConfigFiles.Count > 0)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"{plan.ProtectedConfigFiles.Count} config file(s) on the targets are not part of the publish and are kept:");
            DeploySummary.WriteList(plan.ProtectedConfigFiles);
        }

        DeploySummary.WriteIssues(warnings, plan.Errors);
        if (WriteReport(plan, warnings) is { } report)
        {
            AnsiConsole.MarkupLineInterpolated($"The complete file lists are in [grey]{report}[/]");
        }
    }

    private static string? WriteReport(DeployPlan plan, List<DeployWarning> warnings)
    {
        if (warnings.Count == 0)
        {
            return null;
        }

        var lines = new List<string>();
        foreach (var warning in warnings)
        {
            lines.Add($"[{warning.Code}] {warning.Message}");
            lines.AddRange((warning.Files ?? []).Select(file => $"  {file}"));
            lines.Add(string.Empty);
        }

        if (plan.ProtectedConfigFiles.Count > 0)
        {
            lines.Add("Config files that are not part of the publish and are kept:");
            lines.AddRange(plan.ProtectedConfigFiles.Select(file => $"  {file}"));
        }

        string path = Path.Combine(plan.Package.Directory, ReportFileName);
        try
        {
            File.WriteAllLines(path, lines);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning($"Could not write {path}:\n{e}");
            return null;
        }
    }

    private static async Task AskKeepAsync(
        DeployPlan plan, PackageInventory inventory, ConfigStore<ServerConfig> store, ServerConfig config, CancellationToken ct)
    {
        var targets = config.EnvironmentFor(plan.Package.Manifest.Prefix).Targets
            .Where(t => inventory.Find(t.Key) is not null)
            .OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        string project = targets.Count == 1
            ? targets[0].Key
            : await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>().Title("Keep files of which project?").AddChoices(targets.Select(t => t.Key)), ct);
        string entered = await AnsiConsole.PromptAsync(
            new TextPrompt<string>(
                    $"Enter a [green]keep pattern[/] for [green]{Markup.Escape(project)}[/], like logs/** or *.log (with / it is relative to the "
                    + "target directory, without it matches the name at any depth; leave empty to add none):")
                .AllowEmpty()
                .Validate(value => string.IsNullOrWhiteSpace(value) || KeepPatterns.TryNormalize(value, out _, out _)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Enter a pattern relative to the target directory, without '..'[/]")),
            ct);
        if (string.IsNullOrWhiteSpace(entered) || !KeepPatterns.TryNormalize(entered, out string pattern, out _))
        {
            return;
        }

        targets.First(t => t.Key == project).Value.AddKeep(pattern);
        store.Save(config);
        AnsiConsole.MarkupLineInterpolated($"Saved to [grey]{store.Path}[/]");
    }
}
