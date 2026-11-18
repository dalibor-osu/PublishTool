# CLAUDE.md

Guidance for AI agents (and humans) working in this repository.

> **Keep this file current.** Whenever a change touches anything described here (commands, options, config
> keys, the deploy workflow, file layout, build/release process, conventions), update the relevant section of
> this CLAUDE.md in the same change. Treat an outdated CLAUDE.md as a bug.

## What PublishTool is

PublishTool is a Windows-focused, Native AOT compiled .NET 11 console tool that publishes one or more .NET
projects from a repository with MSBuild and, unless `-l/--local` is given, prepares an incremental deploy package:

1. Builds all selected projects and their project references once (via a generated MSBuild traversal project).
2. Publishes every selected project in parallel into `<publish dir>\<ProjectName>`.
3. Without `--local`: downloads the previous full publish of the current git branch from a shared network
   directory (stored there as a single zip), extracts and compares it locally, writes only the changed files
   into a local deploy directory, and uploads a new zip of the full publish as the reference for next time.

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
  ProjectScanner.cs               Finds *.csproj recursively, detects Sdk / LegacyWeb / LegacyLibrary
  DependencyBuild.cs              Writes a temp traversal .proj that restores + builds all projects with /m
  PublishCommandBuilder.cs        Builds the MSBuild command line per project kind
  DeployPreparer.cs               The deploy logic, skipped with --local (download, compare, zip, upload)
  Console/JobDisplay.cs           JobRow/JobDisplay: the redrawn progress table (spinner, tail of output)
  GitHub/                         GitHubClient (releases API), ReleaseVersion (semver + rc parsing/compare)
  Helpers/                        ProcessHelper (run process, stream output), EnvironmentHelper, OptionParsers
  Logger.cs                       In-memory log flushed to %APPDATA%\PublishTool\Logs\<date>.log on exit
PublishTool.Generators/           Roslyn incremental source generator (netstandard2.0, Scriban templates)
  CommandParserGenerator.cs       Collects [Command] classes + [Option] properties into a model
  Emitter.cs + Templates/*.sbncs  Renders PublishTool.Generated.CommandParsers (parsing + help text)
  Diagnostics.cs                  PT0001..PT0008 compile errors for bad command/option declarations
.github/workflows/release.yml     Manual release: semver from Conventional Commits, AOT build for 6 RIDs
```

There are no automated tests in the repo. Verify changes by building and by running the dev binary
(see below); for `DeployPreparer` logic a throwaway console project that includes the source file works well.

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

- **Native AOT**: no reflection-based serialization or dynamic code. JSON goes through `JsonContext`
  (source generated), command parsing is source generated, regexes use `[GeneratedRegex]`. Keep it that way.
- **Adding a command**: create `Commands/FooCommand.cs` with `[Command("foo", Description = "...")]`
  implementing `ICommand<FooCommandOptions>`, and `Commands/Options/FooCommandOptions.cs` with `[Option]`
  properties. Supported option types are `bool` (flag), `string`, `int` (also nullable), or any type via
  `Parser = nameof(StaticMethod)` with signature `static bool M(string value, TOptions options, out string? error)`.
  `-h/--help` is reserved and added to every command. `IsDefault = true` marks the command that runs when
  none is given (currently `publish`). The generator emits compile errors PT0001-PT0008 for mistakes.
- **UI model**: commands with `UsesAlternateScreen => true` run inside Spectre's alternate screen and wait
  for a key press at the end. `PublishCommand` does not use Spectre live widgets; it redraws a `JobDisplay`
  every 100 ms from a loop in `ExecuteAsync`. Long-running work runs in `Task.Run` and reports progress with
  `JobRow.AppendLine`; a row's `Name` can be changed while it runs to show the current phase.
- **Cancellation**: Ctrl+C cancels the shared `CancellationTokenSource`; MSBuild processes are killed with
  their process tree. Every loop over files must call `ct.ThrowIfCancellationRequested()`. Cleanup of temp
  directories happens in `finally` blocks in `PublishCommand.PrepareDeploy`.
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
  `/p:BuildProjectReferences=false`.
- **Code style**: `.editorconfig` at the root, file-scoped namespaces, primary constructors, collection
  expressions, `Lock` type, C# 14 `extension` blocks (see `StringExtensions`). Nullable is enabled and the
  build should stay warning-free. Source files use CRLF line endings (git `autocrlf=true`); if a tool writes
  LF, convert back so the working copy stays consistent. No XML doc comments and no explanatory comments in
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
   reference; changed or new files are copied to `<deploy dir>\<name>\<Project>\...`. Deleted files are
   not tracked.
3. **Compress**: the full publish is zipped to `<deploy dir>\<name>.tmp\<name>.zip`. Projects that exist in
   the previous reference but were not published this time are carried over from the extracted reference,
   so the reference stays complete for partial publishes. A legacy `Deploy` subdirectory is skipped.
4. **Upload**: the zip is streamed to `<shared>\<name>.zip.uploading` with percent progress, then renamed
   to `<name>.zip`. The `.tmp` work directory is deleted; on failure or cancel the partial upload and the
   deploy directory are deleted as well.
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

## Usage guide (end user)

```
PublishTool [command] [options]

  publish  (default)  -dir <path>  -a  -l|--local
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
2. **Publish**: `PublishTool publish` prompts for projects and configuration (Release/Debug), then shows the
   build row, one row per project and the five deploy rows. `-a` publishes all publishable projects in
   Release without prompts. `-l/--local` only publishes locally and skips the deploy rows. Exit code 0 on
   success, 1 on any failure.
3. **Deploy**: after a publish without `--local`, hand `<deploy dir>\<branch>_<timestamp>` to whoever deploys; it contains
   only the files that differ from the previous publish of the same branch.
4. **Update the tool**: `PublishTool update` (or `update --fetch` to only check). Dev builds cannot update.
   The previous executable is kept as `PublishTool.exe.old` and removed on the next start.
