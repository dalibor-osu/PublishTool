using PublishTool.Deployment;
using Spectre.Console;

namespace PublishTool.Console;

internal static class DeploySummary
{
    private const int MaxListedFiles = 10;

    public static void WritePlan(DeployPlan plan, PackageVerification verification, string toolVersion)
    {
        var manifest = plan.Package.Manifest;
        AnsiConsole.MarkupLineInterpolated(
            $"Package [green]{manifest.Name}[/] (prefix [green]{manifest.Prefix}[/]), created {manifest.Created:yyyy-MM-dd HH:mm:ss} by PublishTool {manifest.ToolVersion}, deployed by PublishTool {toolVersion}");
        var table = new Table()
            .AddColumns("Project", "Target", "Host", "New", "Overwrite", "Delete", "Deployed now", "Based on");
        foreach (var target in plan.Targets)
        {
            table.AddRow(
                Markup.Escape(target.Project.Name),
                Markup.Escape(target.TargetPath),
                Markup.Escape(target.ServiceName is null ? "IIS" : $"service {target.ServiceName}"),
                target.Create.Count.ToString(),
                target.Overwrite.Count.ToString(),
                target.Delete.Count.ToString(),
                Markup.Escape(target.Current?.Package ?? "-"),
                Markup.Escape(target.Project.IsFullPublish ? "full publish" : target.Project.Base ?? "unknown"));
        }

        AnsiConsole.Write(table);
        if (verification.ExtraFiles.Count > 0)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]{verification.ExtraFiles.Count} file(s) in the package are not in the manifest and are not deployed:[/]");
            WriteList(verification.ExtraFiles);
        }

        WriteIssues(plan.Warnings, plan.Errors);
    }

    public static void WriteList(IReadOnlyList<string> items)
    {
        foreach (string item in items.Take(MaxListedFiles))
        {
            AnsiConsole.MarkupLineInterpolated($"  [grey]{item}[/]");
        }

        if (items.Count > MaxListedFiles)
        {
            AnsiConsole.MarkupLineInterpolated($"  [grey]... and {items.Count - MaxListedFiles} more[/]");
        }
    }

    public static void WriteIssues(IReadOnlyList<DeployWarning> warnings, IReadOnlyList<string> errors)
    {
        foreach (var warning in warnings)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]Warning:[/] {warning.Message}");
        }

        foreach (string error in errors)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {error}");
        }
    }

    public static async Task<bool> AcceptAsync(IReadOnlyList<DeployWarning> warnings, string action, CancellationToken ct)
    {
        var prompt = warnings.Count == 0
            ? new ConfirmationPrompt($"{Markup.Escape(action)}?") { DefaultValue = true }
            : new ConfirmationPrompt($"[yellow]{warnings.Count} warning(s).[/] Accept the risk and {Markup.Escape(action.ToLowerInvariant())}?")
            {
                DefaultValue = false
            };
        bool accepted = await AnsiConsole.PromptAsync(prompt, ct);
        if (!accepted)
        {
            AnsiConsole.MarkupLine("Nothing was changed.");
        }

        return accepted;
    }
}
