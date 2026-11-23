# CLAUDE.md

Guidance for AI agents (and humans) working in this repository. Additional info specific to user can be provided in CLAUDE.md.user file, that is ignored in git by purpose. Read it if it exists.

> **Keep this file current.** Whenever a change touches anything described here (commands, options, config
> keys, the deploy workflow, file layout, build/release process, conventions), update the relevant section of
> this CLAUDE.md in the same change. Treat an outdated CLAUDE.md as a bug.

## What PublishTool is

PublishTool is a Windows-focused, Native AOT compiled .NET 11 console tool that publishes one or more .NET
projects from a repository with MSBuild and, unless `-l/--local` is given, prepares an incremental deploy package:

1. Builds all selected projects and their project references once (via a generated MSBuild traversal project).
2. Publishes every selected project in parallel into `<publish dir>\<ProjectName>` (cleared first).
3. Without `--local`: downloads the previous full publish of the current git branch from a shared network
   directory (stored there as a single zip), extracts and compares it locally, writes only the changed files
   plus a manifest (hashes, deleted files, base package) into a local package directory, and uploads a new zip
   of the full publish as the reference for next time.

On the server, `deploy` applies such a package to the IIS apps and Windows services mapped for its branch
prefix (backup, stop, copy and delete, start, with automatic rollback on any failure), and `rollback` undoes
a deploy. See "The server deploy" below; the design is in `docs/specs/2026-11-23-server-deploy-command.md`.

It also updates itself from GitHub releases (`dalibor-osu/PublishTool`). The interactive UI uses Spectre.Console.

## Repository layout

```
PublishTool.slnx                  Solution (two projects)
Directory.Build.props             Repo-wide MSBuild props: references SonarAnalyzer.CSharp in both projects
PublishTool/                      The CLI (net11.0, PublishAot, InvariantGlobalization)
  Program.cs                      Entry point: Ctrl+C -> CancellationToken, parse args, run command, write log
  Commands/                       One class per command, [Command("name")] + ICommand<TOptions>
    Options/                      One options class per command, properties marked with [Option(...)]
    PublishCommand.cs             The main workflow (build, publish, deploy step) and the live progress UI
    DeployCommand.cs              Server side: verify a package, plan, confirm, run DeployRunner (Windows, admin)
    RollbackCommand.cs            Server side: restore a deployed package's backup, run RollbackRunner (Windows, admin)
    ConfigCommand.cs              Forces the interactive config wizard for a directory
    UpdateCommand.cs              Self-update from GitHub releases (download, extract, swap exe, .old backup)
    VersionCommand.cs / HelpCommand.cs
  Attributes/                     CommandAttribute, OptionAttribute (consumed by the source generator)
  Configuration/                  Config file handling
    Config.cs                     Current config model (format version 2): Version + Directories map of DirectorySettings
    ConfigHandler.cs              Load(workingDir) = load file + wizard for missing settings, returns the DirectorySettings
    ConfigStore.cs                Load/save with version detection, migrator chain, backup of migrated files
    ConfigVersions.cs             Registry: Current version, parsers per version, migrators per step
    ConfigParser.cs               IConfigParser + ConfigParser<TModel> (one per readable version)
    ConfigMigrator.cs             IConfigMigrator + ConfigMigrator<TFrom,TTo> base class (one per version step)
    ConfigEnvelope.cs             Only the Version field, read first to pick the parser
    IVersionedConfig.cs           Implemented by the current model so the store can stamp the version
    ConfigException.cs            User-facing config error, printed in red by Program.cs
    JsonContext.cs                Source-generated JSON metadata for every readable config model
    Versions/ConfigV1.cs          Frozen snapshot of format version 1 (five parallel dictionaries); never edit
    Migrations/ConfigV1ToV2.cs    Migrator 1 -> 2 (merges the dictionaries per directory)
    ServerConfig.cs               Server settings (own format version): Environments per prefix + Deployed records per path
    ServerConfigVersions.cs       Registry of the server settings file (parsers, migrators), separate from ConfigVersions
    ServerConfigHandler.cs        ResolveTargets: wizard for missing target paths / service names of a prefix
  Deployment/                     Package manifest, lineage and everything the deploy and rollback commands do
    PackageManifest.cs            publishtool-manifest.json model (files with size + SHA-256, deletions, base per project)
    ReferenceLineage.cs           publishtool-lineage.json in the reference zip: project -> package that last published it
    PackageJson.cs                Read/write of manifest, lineage and journal with format checks (temp file + move)
    PackagePaths.cs               Manifest path <-> native path, Resolve() rejects paths escaping the root
    DeployPackage.cs              Loads a package directory, verifies files against the manifest, lists extra files
    DeployPlan.cs                 Per target: files to create / overwrite / delete, chain and version warnings, errors
    DeployJournal.cs              <package>\.backup\journal.json model (state, backed up and created files, previous record)
    DeployExecutor.cs             Backup, Apply and the idempotent Restore
    HostSet.cs                    Which of IIS / services to stop and start again, in the right order
    IHostController.cs            Seam for IIS and service control; WindowsHostController is the real one
    DeployRunner.cs / RollbackRunner.cs   JobDisplay rows and the flow including rollback on failure
  ProjectScanner.cs               Finds *.csproj recursively, detects Sdk / LegacyWeb / LegacyLibrary
  DependencyBuild.cs              Writes a temp traversal .proj that restores + builds all projects with /m
  PublishCommandBuilder.cs        Builds the MSBuild command line per project kind
  DeployPreparer.cs               The deploy logic, skipped with --local (download, compare, zip, upload)
  Console/JobDisplay.cs           JobRow/JobDisplay: the redrawn progress table (spinner, tail of output)
  Console/JobDisplayRunner.cs     Redraw loop for a JobDisplay while work runs (live) or a single render at the end
  Console/DeploySummary.cs        Summary table, warnings/errors and the accept/confirm prompt of deploy and rollback
  Console/BackKeyConsole.cs       IAnsiConsole wrapper whose input throws BackRequestedException on Esc / Left arrow
  GitHub/                         GitHubClient (releases API), ReleaseVersion (semver + rc parsing/compare)
  Helpers/                        ProcessHelper (run process, stream output), EnvironmentHelper, OptionParsers,
                                  FileHashing, WindowsAccount (admin check), CriticalSection (swallows Ctrl+C)
  Logger.cs                       In-memory log flushed to %APPDATA%\PublishTool\Logs\<date>.log on exit
PublishTool.Generators/           Roslyn incremental source generator (netstandard2.0, Scriban templates)
  CommandParserGenerator.cs       Collects [Command] classes + [Option] properties into a model
  Emitter.cs + Templates/*.sbncs  Renders PublishTool.Generated.CommandParsers (parsing + help text)
  Diagnostics.cs                  PT0001..PT0008 compile errors for bad command/option declarations
.github/workflows/release.yml     Manual release: semver from Conventional Commits, AOT build for 6 RIDs
TestApps/                         Four sample apps (Core/Framework x IIS/service) + scripts for an end-to-end deploy test
docs/specs/                       Agreed design specifications
```

There are no automated tests in the repo. Verify changes by building and by running the dev binary
(see below); for `DeployPreparer` logic a throwaway console project that includes the source file works well.
For deploy and rollback logic, a throwaway console project that compiles `PublishTool/**/*.cs` (minus
`Program.cs`, plus its own `BuildInfo` class) can drive `DeployPlan`, `DeployRunner` and `RollbackRunner` with
a fake `IHostController` and a `ConfigStore<ServerConfig>` pointed at a sandbox path, without administrator
rights. `TestApps/README.md` has the elevated end-to-end test with real IIS sites and services. `publish`
needs a real terminal (alternate screen); `deploy`/`rollback` with `-y` run without one.

## Development commands

Requirements: .NET SDK 11.x (`dotnet --version`), and MSBuild on `PATH` to actually run `publish`
(Visual Studio / Build Tools; the tool calls `MSBuild` by name, not `dotnet build`).

```powershell
# Build (Debug produces PublishTool-dev.exe, version "dev")
dotnet build PublishTool/PublishTool.csproj -c Debug

# Full static analysis pass: forces a recompile so every Sonar (S####) and compiler warning is reported
dotnet build PublishTool.slnx -c Debug --no-incremental

# Run the dev binary
PublishTool\bin\Debug\net11.0\PublishTool-dev.exe --help
PublishTool\bin\Debug\net11.0\PublishTool-dev.exe publish -dir C:\path\to\repo
PublishTool\bin\Debug\net11.0\PublishTool-dev.exe config -dir C:\path\to\repo
PublishTool\bin\Debug\net11.0\PublishTool-dev.exe deploy --dry-run -dir C:\path\to\deploy\uat_2026-01-01_12-00-00

# Native AOT publish like CI does (needs the VC++ toolchain on Windows)
dotnet publish PublishTool/PublishTool.csproj -c Release -r win-x64 --self-contained -p:PublishAot=true -p:BuildVersion=1.2.3 -o artifacts/win-x64

# Inspect what the source generators emitted (EmitCompilerGeneratedFiles is on)
Get-ChildItem PublishTool/obj/generated -Recurse -Filter *.g.cs
```

`dotnet build` of the solution also rebuilds the generator; if generated code looks stale, delete
`PublishTool/obj` and build again.

### Releasing

Releases are cut manually with the **Release** GitHub Actions workflow (`workflow_dispatch`). It derives the
next version from Conventional Commits since the last tag (or takes an explicit bump/version), builds Native
AOT binaries for win-x64/arm64, linux-x64/arm64, osx-x64/arm64, packages them as
`PublishTool-v<version>-<rid>.zip|tar.gz` (+ `.sha256`), tags `v<version>` and creates the GitHub release.
The `update` command downloads exactly these asset names, so keep the naming in `release.yml`,
`ReleaseVersion.GetDownloadUrl` and `EnvironmentHelper` in sync. Use Conventional Commit messages
(`feat:`, `fix:`, `feat!:`/`BREAKING CHANGE`) so `auto` bump works.

## Architecture notes and conventions

- Always add newly created files to git (git add)
- **Native AOT**: no reflection-based serialization or dynamic code. JSON goes through `JsonContext`
  (source generated), command parsing is source generated, regexes use `[GeneratedRegex]`. Keep it that way.
- **Adding a command**: create `Commands/FooCommand.cs` with `[Command("foo", Description = "...")]`
  implementing `ICommand<FooCommandOptions>`, and `Commands/Options/FooCommandOptions.cs` with `[Option]`
  properties. Supported option types are `bool` (flag), `string`, `int` (also nullable), or any type via
  `Parser = nameof(StaticMethod)` with signature `static bool M(string value, TOptions options, out string? error)`.
  `-h/--help` is reserved and added to every command. `IsDefault = true` marks the command that runs when
  none is given (currently `publish`). The generator emits compile errors PT0001-PT0008 for mistakes.
- **UI model**: commands with `UsesAlternateScreen => true` run inside Spectre's alternate screen and wait
  for a key press at the end. The commands do not use Spectre live widgets; `JobDisplayRunner` redraws a
  `JobDisplay` every 100 ms while the work task runs. Long-running work runs in `Task.Run` and reports progress
  with `JobRow.AppendLine`; a row's `Name` can be changed while it runs to show the current phase. `deploy` and
  `rollback` set `UsesAlternateScreen => !Options.Yes`: with `-y` they print to the normal console, render the
  display once at the end and do not wait for a key, so they can run from scripts.
- **Going back in prompts**: Spectre prompts have no back key, so a prompt that should support it is shown on
  a `BackKeyConsole` wrapping `AnsiConsole.Console`; its input throws `BackRequestedException` on Esc or
  Left arrow (see `PublishCommand.PromptConfiguration`). Spectre does not clean up a prompt stopped this way
  (its lines stay and the cursor stays hidden), so the catch calls `AnsiConsole.Clear()` and
  `AnsiConsole.Cursor.Show()`. Don't use it on prompts where Left arrow means something (hierarchical
  multi-selection, text input).
- **Cancellation**: Ctrl+C cancels the shared `CancellationTokenSource`; MSBuild processes are killed with
  their process tree. Every loop over files must call `ct.ThrowIfCancellationRequested()`. Cleanup of temp
  directories happens in `finally` blocks in `PublishCommand.PrepareDeploy`. While a `CriticalSection` is
  entered (deploy from stopping the hosts to the end of a restore, the whole rollback run), `Program.cs` swallows
  every further Ctrl+C instead of letting the second one kill the process; the first one still cancels the
  token, which a deploy treats as a failure and rolls back.
- **Errors**: user-facing messages are short; full exceptions go to `Logger.LogError`. The log is written to
  `%APPDATA%\PublishTool\Logs\yyyy-MM-dd.log` only if something was logged, and the path is printed.
- **Process output**: use `ProcessHelper.RunAsync`; it streams stdout/stderr lines to a callback and returns
  exit code plus captured output.
- **Config**: `%APPDATA%\PublishTool\config.json`: `Version` plus `Directories`, a map from the absolute
  working directory to its `DirectorySettings` (`PublishDirectory`, `SharedDirectory`, `DeployDirectory`,
  `IgnoredDirectories`, `PublishableProjects`). A setting that is `null`/absent was never asked for;
  `ConfigHandler.Load(workingDir)` runs the wizard for it, saves, and returns the directory's
  `DirectorySettings`. `DirectorySettings.IsComplete` (annotated with `MemberNotNullWhen`) tells consumers
  everything is set; `ProjectScanner.Scan` takes the `DirectorySettings?` directly. Adding a new per-directory
  setting means adding a nullable property to `DirectorySettings`, including it in `IsComplete`, and prompting
  for it in `ConfigHandler.RunWizard`. That is **not** a new format version.
- **Server config**: `%ProgramData%\PublishTool\server.json` (machine-wide, written by elevated deploys),
  model `ServerConfig`, own registry `ServerConfigVersions` and store `ConfigStore.Server`, same versioning
  machinery and recipe as `config.json` but its own version number. `Environments` maps a package prefix to
  `Targets` (project name -> `Path`, optional `ServiceName`; no service name means an IIS app); `Deployed`
  maps an absolute target path to the last package deployed there. Lookups are case-insensitive
  (`ServerConfig.FindKey`). Missing targets are asked for by `ServerConfigHandler.ResolveTargets`, which only
  accepts an absolute path and a service name that exists on the machine (the check is passed in, so the
  handler stays platform-neutral); there is no editing wizard yet, so a wrong path is fixed in the file.
- **Config format versions**: the file carries `Version` (files without it are version 1). `ConfigStore`
  reads the version, parses with that version's `ConfigParser`, runs the `ConfigMigrator` chain step by step
  to `ConfigVersions.Current`, then backs the old file up as `config.v<N>.json.bak` and saves the upgraded
  one. A file newer than the tool is rejected with a `ConfigException` (shown in red by `Program.cs`); there
  is no downgrade. Bump the version only when the old model can no longer read the file (renamed/reshaped
  properties). Version 2 is the worked example: version 1 stored five parallel dictionaries
  (`Versions/ConfigV1.cs`), `Migrations/ConfigV1ToV2.cs` merges them per directory and leaves settings a
  directory never had as `null`. The store validates at startup that every version has a parser and every
  step a migrator, so registry mistakes fail on the first run. Recipe for a new version N:
  1. Copy the current `Config` (and `DirectorySettings`) to `Versions/ConfigV<N-1>.cs` as a frozen snapshot:
     plain classes, no `IVersionedConfig`, `Version` as a plain int, no helpers.
  2. Change `Config` to the new shape, set `ConfigVersions.Current = N`, fix the wizard and the consumers.
  3. Register the snapshot in `JsonContext`, add `new ConfigParser<ConfigV<N-1>>(N-1, JsonContext.Default.ConfigV<N-1>)`
     to `ConfigVersions.Parsers`, and change the current model's parser entry to version N.
  4. Add `Migrations/ConfigV<N-1>ToV<N>.cs` deriving from `ConfigMigrator<ConfigV<N-1>, Config>` and add it to
     `ConfigVersions.Migrators`. Older migrators keep targeting their snapshot (`ConfigMigrator<ConfigV1, ConfigV2>`
     once version 3 exists), so migration always runs one step at a time.
  5. Verify with a throwaway console project that includes `Configuration/**/*.cs` (minus `ConfigHandler.cs`)
     and feeds a real old file through a `ConfigStore<Config>` pointed at a sandbox path.
- **Project kinds** (`ProjectScanner.DetectKind`): SDK-style projects are published with
  `/t:Publish /p:RuntimeIdentifier=win-x64 /p:SelfContained=false`; legacy web projects
  (`Microsoft.WebApplication.targets`) use `DeployOnBuild` + `WebPublishMethod=FileSystem`; other legacy
  projects just build with `OutDir`. Project references are built once up front, then each publish runs with
  `/p:BuildProjectReferences=false`. `<publish dir>\<Project>` is deleted before each publish, because
  `dotnet publish` never removes stale files and deleted files would otherwise never show up as deletions.
  SDK projects embed the git commit in their assemblies, so their dll/exe/pdb change with every commit.
- **Code style**: `.editorconfig` at the root, file-scoped namespaces, primary constructors, collection
  expressions, `Lock` type, C# 14 `extension` blocks (see `StringExtensions`). Nullable is enabled and the
  build should stay warning-free. All text files use LF line endings, in the repository and in the working
  copy: `.gitattributes` sets `* text=auto eol=lf` (only `*.cmd`/`*.bat` are CRLF) and `.editorconfig` sets
  `end_of_line = lf`. If a tool writes CRLF, convert the file back to LF. No XML doc comments and no explanatory comments in
  code (the owner's preference); names carry the meaning, and anything that needs explaining goes into this
  file instead. Interface members carry an explicit `public` modifier (see `ICommand`), and classes implement
  them as normal `public` members, never with explicit interface implementation syntax (`void IFoo.Bar()`).
  Keep methods under Sonar's cognitive complexity limit of 15 (S3776) by splitting them, as in
  `ConfigHandler.RunWizard`.
- **Static analysis**: `SonarAnalyzer.CSharp` (the same rules SonarQube for IDE shows in Rider) is referenced from
  `Directory.Build.props` and runs on every build, so Sonar findings appear as `S####` compiler warnings.
  **Before finishing any work, run the full analysis build from the commands above and make sure it reports
  0 warnings.** Fix findings rather than silencing them. For a real false positive, suppress as narrowly as
  possible (`[SuppressMessage]` with a `Justification` on the member, see `Emitter.cs`); a rule that does not
  fit this project at all is disabled in the Sonar section of `.editorconfig` with a comment saying why.
- **Known gap**: `update -v/--version` is parsed but `UpdateCommand` never uses `Options.Version`
  (it always installs the latest full or pre-release version).

## The deploy workflow (DeployPreparer)

Runs after every publish unless `-l/--local` is given (the flag replaced the old opt-in `-c/--complete`).

Naming: `<prefix>_<yyyy-MM-dd_HH-mm-ss>` where prefix is the sanitized current git branch (`DEPLOY` if
not in a git repo). The shared directory contains **only zip files** named `<name>.zip`, project
directories at the archive root (`Web/...`, `Api/...`), created with `CompressionLevel.Fastest`.

Steps, each its own row in the UI (five deploy rows in total):

1. **Download**: `FindPreviousReference(prefix)` picks the newest `<prefix>_*.zip`. It is copied to
   `<deploy dir>\<name>.tmp\previous.zip` and extracted to `<deploy dir>\<name>.tmp\previous\`. If a
   directory (old workflow) with the prefix is newer than the newest zip, the run fails and tells the user to
   zip or delete it, because ignoring it would produce an "everything changed" deploy.
2. **Collect changes**: every file of every published project is compared byte-wise with the extracted
   reference; changed or new files are copied to `<deploy dir>\<name>\<Project>\...` and listed in the
   manifest with size and SHA-256. Files in the reference but missing from the new publish are listed as
   deletions (case-insensitive, so a case-only rename is never a deletion). A project with no changes and no
   deletions is left out of the manifest. Each project's `Base` is the package that last published it,
   read from the reference's lineage; `IsFullPublish` is set when the reference did not contain the project.
   The manifest is written to the `.tmp` work directory here.
3. **Compress**: the full publish is zipped to `<deploy dir>\<name>.tmp\<name>.zip`. Projects that exist in
   the previous reference but were not published this time are carried over from the extracted reference,
   so the reference stays complete for partial publishes. A legacy `Deploy` subdirectory is skipped. The zip
   root gets `publishtool-lineage.json`: project -> the package that last changed it (this package for
   changed projects, the previous value for unchanged and carried projects).
4. **Upload**: the zip is streamed to `<shared>\<name>.zip.uploading` with percent progress, then renamed
   to `<name>.zip`. Only then is the manifest moved to `<deploy dir>\<name>\publishtool-manifest.json`
   (if that fails, the new reference is deleted again), so a package without a committed reference never
   has a manifest and `deploy` refuses it. The `.tmp` work directory is deleted; on failure or cancel the
   partial upload and the deploy directory are deleted as well.
5. **Prune**: after the new zip is in place, all but the newest `KeepReferences` (3, constant in
   `PublishCommand`) archives of the same prefix are deleted from the share. Only `.zip` files whose name
   parses as `<prefix>_<timestamp>` are touched; old-workflow directories, other branches and `.uploading`
   leftovers are ignored. A failed delete is logged and shown but does not fail the publish.

Design intent: the shared directory is on a slow remote server, so it must only ever see whole-file
copies of one zip (no per-file IO, no remote extraction). All heavy work happens on the local machine.

Migrating old directory-based references to zips (run where the share is fast, e.g. on the server):

```powershell
$shared = '\\server\share\path'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Get-ChildItem $shared -Directory | Where-Object { $_.Name -match '_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$' } | ForEach-Object {
    $zip = "$($_.FullName).zip"
    if (Test-Path $zip) { return }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($_.FullName, "$zip.uploading", 'Fastest', $false)
    Rename-Item "$zip.uploading" $zip
    Remove-Item $_.FullName -Recurse -Force
}
```

## The server deploy (deploy / rollback)

Windows only (other platforms exit 1 at once); both need administrator rights, except `deploy --dry-run`.
Everything is driven by the manifest: a directory or file not listed there is never deployed (extra files are
shown in the summary), and every manifest path goes through `PackagePaths.Resolve`, which rejects paths
outside the package or target.

`deploy -dir <package>` preflight, nothing changes on the server: admin check, refuse if `<package>\.backup`
exists (deployed before or interrupted, run `rollback` first), load the manifest (missing or newer format is
refused), verify every file's size and SHA-256, resolve the targets of the manifest's prefix (wizard for missing
ones), build the `DeployPlan`. Errors stop it: two projects mapped to one path, a missing service, IIS targets
without IIS. Warnings need the user's acceptance (default no): the package was made by another PublishTool
version (exact string match with `BuildInfo.Version`), a target directory does not exist, and the chain check
per target: the target's `Deployed` record must equal the project's `Base`; an unknown base, a missing record,
a mismatch (skipped or out-of-order package), the package already deployed there, or a full publish onto a
deployed target all warn. `-y` skips only the final confirmation and stops with exit 1 on any warning or
missing mapping; `--dry-run` stops after the summary.

`DeployRunner` rows: backup (overwritten and deleted files into `<package>\.backup\<Project>\...`, journal
`journal.json` written before and after, state `BackingUp`), stop (journal state `Applying` with the hosts that
were running and will be started again; services first, then `iisreset /stop` if any target has no service
name), apply (copy, delete, remove directories the deletions left empty), start (`iisreset /start`, then the
services; only what was running before), record (`Deployed` records + journal state `Deployed`). Any failure
or Ctrl+C after the backup restores the backup (`DeployExecutor.Restore`: copy backed-up files back, delete
created files and empty created directories; idempotent, so it does not need to know how far apply got),
resets the records if they were written, starts the hosts again and removes the backup. Before restoring after
the apply started, it stops the hosts again (a failed start may have started some of them, and running apps
lock their files); if the failure came before apply, nothing was changed, so it skips the restore. If the
restore itself fails, the journal becomes `RestoreFailed`, the backup stays and the user runs `rollback`. A
failed or cancelled backup removes only a backup this run created.

`rollback -dir <package>` reads the journal: `BackingUp` means nothing on the server changed, so it only removes
the backup. Otherwise it warns when a target's record is not this package (a newer deploy happened; skipped for
`Applying`, an interrupted deploy, and `RestoreFailed`), stops the running hosts, restores, starts what it
stopped plus, for `Applying` and `RestoreFailed`, what the deploy had stopped, resets the records to the
journal's previous ones and deletes the backup. Backups are otherwise never pruned; deleting a package directory deletes its backup.

## Usage guide (end user)

```
PublishTool [command] [options]

  publish  (default)  -dir <path>  -a  -l|--local
  deploy              -dir <package dir>  -y|--yes  --dry-run   (server, elevated)
  rollback            -dir <package dir>  -y|--yes              (server, elevated)
  config              -dir <path>
  update              -pre|--pre-release  --fetch  (-v|--version <version>: currently ignored)
  version
  -h, --help          on every command
```

1. **First run in a repository**: `PublishTool -dir C:\repo` (or run it from the repo). The wizard asks for
   the publish directory (local output, one subdirectory per project), the shared directory (network share
   holding the reference zips), the deploy directory (local, receives `<name>` change sets), directories to
   ignore while scanning (default `.git, .claude`), and which projects are publishable. Re-run the wizard
   with `PublishTool config -dir C:\repo`.
2. **Publish**: `PublishTool publish` prompts for projects and configuration (Release/Debug). Pressing Esc or
   Left arrow in the configuration prompt reopens the project prompt with the previous selection preselected.
   Then it shows the build row, one row per project and the five deploy rows. `-a` publishes all publishable projects in
   Release without prompts. `-l/--local` only publishes locally and skips the deploy rows. Exit code 0 on
   success, 1 on any failure.
3. **Deploy**: after a publish without `--local`, copy `<deploy dir>\<branch>_<timestamp>` to the server; it
   contains only the files that differ from the previous publish of the same branch plus the manifest. On the
   server, in an elevated terminal, run `PublishTool deploy -dir <package>`. The first package of a prefix asks
   for each project's target directory and Windows service name (empty for IIS apps), stored in
   `%ProgramData%\PublishTool\server.json`. Review the summary and warnings, confirm, and watch the rows.
   Deploy packages of a branch in order; the tool warns when one was skipped. Config files are deployed like
   any other file, so the app itself must ship correct per-environment configs. Keep the package directory:
   it holds the backup. `PublishTool rollback -dir <package>` undoes the deploy.
4. **Update the tool**: `PublishTool update` (or `update --fetch` to only check). Dev builds cannot update.
   The previous executable is kept as `PublishTool.exe.old` and removed on the next start. Without `-pre` the
   target is the newest GitHub release not marked as a pre-release; with `-pre` it is the newest release of
   any kind, so a full release wins over older pre-releases. Users running a pre-release are asked whether to
   include pre-releases. If the installed version is newer than the target (a pre-release ahead of the latest
   full release), `--fetch` prints a downgrade warning and `update` asks before downgrading (default no).
