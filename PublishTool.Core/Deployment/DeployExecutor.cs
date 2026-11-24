namespace PublishTool.Deployment;

public static class DeployExecutor
{
    private const int ReportEvery = 25;

    public static DeployJournal CreateJournal(DeployPlan plan, string toolVersion) => new()
    {
        Package = plan.Package.Manifest.Name,
        ToolVersion = toolVersion,
        State = JournalState.BackingUp,
        Started = DateTimeOffset.Now,
        Targets =
        [
            .. plan.Targets.Select(t => new JournalTarget
            {
                Project = t.Project.Name,
                Path = t.TargetPath,
                ServiceName = t.ServiceName,
                Previous = t.Current,
                Created = [.. t.Create],
                CreatedDirectories = [.. t.CreatedDirectories]
            })
        ]
    };

    public static int Backup(DeployPlan plan, DeployJournal journal, Action<string> report, CancellationToken ct)
    {
        if (Directory.Exists(plan.Package.BackupDirectory))
        {
            throw new DeployException($"{plan.Package.BackupDirectory} already exists; roll back the previous deploy of this package first.");
        }

        Directory.CreateDirectory(plan.Package.BackupDirectory);
        PackageJson.WriteJournal(plan.Package.JournalPath, journal);
        int total = plan.Targets.Sum(t => t.Overwrite.Count + t.Delete.Count);
        int done = 0;
        foreach (var (target, journalTarget) in plan.Targets.Zip(journal.Targets))
        {
            string backupRoot = Path.Combine(plan.Package.BackupDirectory, target.Project.Name);
            foreach (string file in target.Overwrite.Concat(target.Delete))
            {
                ct.ThrowIfCancellationRequested();
                CopyFile(PackagePaths.Resolve(target.TargetPath, file), PackagePaths.Resolve(backupRoot, file));
                journalTarget.BackedUp.Add(file);
                Progress(++done, total, "backed up", report);
            }
        }

        PackageJson.WriteJournal(plan.Package.JournalPath, journal);
        return done;
    }

    public static (int Copied, int Deleted) Apply(DeployPlan plan, Action<string> report, CancellationToken ct)
    {
        int total = plan.Targets.Sum(t => t.Project.Files.Count + t.Delete.Count);
        int copied = 0;
        int deleted = 0;
        foreach (var target in plan.Targets)
        {
            Directory.CreateDirectory(target.TargetPath);
            foreach (string file in target.Project.Files.Select(f => f.Path))
            {
                ct.ThrowIfCancellationRequested();
                CopyFile(plan.Package.FilePath(target.Project, file), PackagePaths.Resolve(target.TargetPath, file));
                Progress(++copied + deleted, total, "copied or deleted", report);
            }

            foreach (string file in target.Delete)
            {
                ct.ThrowIfCancellationRequested();
                string path = PackagePaths.Resolve(target.TargetPath, file);
                File.Delete(path);
                RemoveEmptyParents(path, target.TargetPath);
                Progress(copied + ++deleted, total, "copied or deleted", report);
            }
        }

        return (copied, deleted);
    }

    public static IReadOnlyList<string> Restore(string backupDirectory, DeployJournal journal, Action<string> report)
    {
        var errors = new List<string>();
        foreach (var target in journal.Targets)
        {
            string backupRoot = Path.Combine(backupDirectory, target.Project);
            foreach (string file in target.BackedUp)
            {
                Attempt(errors, $"{target.Project}/{file}", () => CopyFile(PackagePaths.Resolve(backupRoot, file), PackagePaths.Resolve(target.Path, file)));
            }

            foreach (string file in target.Created)
            {
                Attempt(errors, $"{target.Project}/{file}", () => File.Delete(PackagePaths.Resolve(target.Path, file)));
            }

            foreach (string directory in target.CreatedDirectories)
            {
                Attempt(errors, directory, () =>
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                });
            }

            report($"{target.Project}: {target.BackedUp.Count} files restored, {target.Created.Count} new files removed");
        }

        return errors;
    }

    private static void Attempt(List<string> errors, string item, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Logger.LogError($"Restoring {item} failed:\n{e}");
            errors.Add($"{item}: {e.Message}");
        }
    }

    private static void RemoveEmptyParents(string file, string root)
    {
        for (string? directory = Path.GetDirectoryName(file);
             directory is not null && directory.Length > root.Length && !Directory.EnumerateFileSystemEntries(directory).Any();
             directory = Path.GetDirectoryName(directory))
        {
            Directory.Delete(directory);
        }
    }

    private static void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static void Progress(int done, int total, string what, Action<string> report)
    {
        if (done % ReportEvery == 0 || done == total)
        {
            report($"{done}/{total} files {what}");
        }
    }
}
