# CLAUDE.md

Guidance for AI agents (and humans) working in this repository.

> **Keep this file current.** Whenever a change touches anything described here (commands, options, config
> keys, the deploy workflow, file layout, build/release process, conventions), update the relevant section of
> this CLAUDE.md in the same change. Treat an outdated CLAUDE.md as a bug.

## What PublishTool is

PublishTool is a Windows-focused, Native AOT compiled .NET 10 console tool that publishes one or more .NET
projects from a repository with MSBuild and, optionally, prepares an incremental deploy package:

1. Builds all selected projects and their project references once (via a generated MSBuild traversal project).
2. Publishes every selected project in parallel into `<publish dir>\<ProjectName>`.
3. With `--complete`: downloads the previous full publish of the current git branch from a shared network
   directory (stored there as a single zip), extracts and compares it locally, writes only the changed files
   into a local deploy directory, and uploads a new zip of the full publish as the reference for next time.

It also updates itself from GitHub releases (`dalibor-osu/PublishTool`). The interactive UI uses Spectre.Console.

## Repository layout

```
PublishTool.slnx                  Solution (two projects)
PublishTool/                      The CLI (net10.0, PublishAot, InvariantGlobalization)
  Program.cs                      Entry point: Ctrl+C -> CancellationToken, parse args, run command, write log
  Commands/                       One class per command, [Command("name")] + ICommand<TOptions>
    Options/                      One options class per command, properties marked with [Option(...)]
    PublishCommand.cs             The main workflow (build, publish, deploy step) and the live progress UI
    ConfigCommand.cs              Forces the interactive config wizard for a directory
    UpdateCommand.cs              Self-update from GitHub releases (download, extract, swap exe, .old backup)
    VersionCommand.cs / HelpCommand.cs
  Attributes/                     CommandAttribute, OptionAttribute (consumed by the source generator)
  Config.cs / ConfigHandler.cs    Config model + interactive wizard; JSON via source-generated JsonContext
  ProjectScanner.cs               Finds *.csproj recursively, detects Sdk / LegacyWeb / LegacyLibrary
  DependencyBuild.cs              Writes a temp traversal .proj that restores + builds all projects with /m
  PublishCommandBuilder.cs        Builds the MSBuild command line per project kind
  DeployPreparer.cs               The --complete deploy logic (download, compare, zip, upload)
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

Requirements: .NET SDK 10.x (`dotnet --version`), and MSBuild on `PATH` to actually run `publish`
(Visual Studio / Build Tools; the tool calls `MSBuild` by name, not `dotnet build`).

```powershell
# Build (Debug produces PublishTool-dev.exe, version "dev")
dotnet build PublishTool/PublishTool.csproj -c Debug

# Run the dev binary
PublishTool\bin\Debug\net10.0\PublishTool-dev.exe --help
PublishTool\bin\Debug\net10.0\PublishTool-dev.exe publish -dir C:\path\to\repo
PublishTool\bin\Debug\net10.0\PublishTool-dev.exe config -dir C:\path\to\repo

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
- **Config**: `%APPDATA%\PublishTool\config.json`, keyed by the absolute working directory. Keys:
  `PublishDirectories`, `SharedDirectories`, `DeployDirectories`, `IgnoredDirectories`, `PublishableProjects`.
  `ConfigHandler.Load` runs the interactive wizard for any missing key, so adding a new per-directory setting
  means adding a dictionary to `Config`, a prompt in `HandleDirConfig`, and extending `needsUpdate`.
- **Project kinds** (`ProjectScanner.DetectKind`): SDK-style projects are published with
  `/t:Publish /p:RuntimeIdentifier=win-x64 /p:SelfContained=false`; legacy web projects
  (`Microsoft.WebApplication.targets`) use `DeployOnBuild` + `WebPublishMethod=FileSystem`; other legacy
  projects just build with `OutDir`. Project references are built once up front, then each publish runs with
  `/p:BuildProjectReferences=false`.
- **Code style**: `.editorconfig` at the root, file-scoped namespaces, primary constructors, collection
  expressions, `Lock` type, C# 14 `extension` blocks (see `StringExtensions`). Nullable is enabled and the
  build should stay warning-free. Source files use CRLF line endings (git `autocrlf=true`); if a tool writes
  LF, convert back so the working copy stays consistent.
- **Known gap**: `update -v/--version` is parsed but `UpdateCommand` never uses `Options.Version`
  (it always installs the latest full or pre-release version).

## The `--complete` deploy workflow (DeployPreparer)

Naming: `<prefix>_<yyyy-MM-dd_HH-mm-ss>` where prefix is the sanitized current git branch (`DEPLOY` if
not in a git repo). The shared directory contains **only zip files** named `<name>.zip`, project
directories at the archive root (`Web/...`, `Api/...`), created with `CompressionLevel.Fastest`.

Steps, each its own row in the UI:

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

  publish  (default)  -dir <path>  -a  -c|--complete
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
   build row, one row per project and, with `--complete`, the four deploy rows. `-a` publishes all
   publishable projects in Release without prompts. Exit code 0 on success, 1 on any failure.
3. **Deploy**: after `--complete`, hand `<deploy dir>\<branch>_<timestamp>` to whoever deploys; it contains
   only the files that differ from the previous publish of the same branch.
4. **Update the tool**: `PublishTool update` (or `update --fetch` to only check). Dev builds cannot update.
   The previous executable is kept as `PublishTool.exe.old` and removed on the next start.
