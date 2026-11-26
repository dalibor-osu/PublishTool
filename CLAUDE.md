# CLAUDE.md

Guidance for AI agents (and humans) working in this repository. Additional info specific to user can be provided in CLAUDE.md.user file, that is ignored in git by purpose. Read it if it exists.

> **Keep this file current.** Whenever a change touches anything described here (commands, options, config
> keys, the deploy workflow, file layout, build/release process, conventions), update the relevant section of
> this CLAUDE.md in the same change. Treat an outdated CLAUDE.md as a bug. The same holds for
> `skills/publishtool/SKILL.md`, the skill agents use PublishToolCli from: when behavior it describes changes,
> update it in the same change (see "Repository layout").

## What PublishTool is

PublishTool is a Windows-focused, Native AOT compiled .NET 11 tool that publishes one or more .NET projects from
a repository with MSBuild and, unless `-l/--local` is given, prepares an incremental deploy package:

1. Builds all selected projects and their project references once (via a generated MSBuild traversal project).
   Every MSBuild invocation gets the package prefix (the sanitized git branch) as the global property
   `PublishToolPrefix`, so the published repository can choose its per-environment configs from it.
2. Publishes every selected project in parallel into `<publish dir>\<ProjectName>` (cleared first).
3. Without `--local`: downloads the previous full publish of the current git branch from a shared network
   directory (stored there as a single zip), extracts and compares it locally, writes only the changed files
   plus a manifest (hashes of new and replaced files, deleted files, base package) and an inventory (every file
   of the full publish with size and hash) into a local package directory, and uploads a new zip of the full
   publish as the reference for next time.

On the server, `deploy` applies such a package to the IIS apps and Windows services mapped for its branch
prefix (backup, stop, copy and delete, start, with automatic rollback on any failure), and `rollback` undoes
a deploy. On request deploy also cleans the targets with the inventory: it deletes files that are not part of the
publish and reports files that differ or are missing. See "The server deploy" below; the design is in
`docs/specs/2026-11-23-server-deploy-command.md`.

It ships as two independent executables over one core library (design: `docs/specs/2026-11-24-non-interactive-cli.md`):

- **PublishTool**: the interactive TUI (Spectre.Console prompts, wizards, a redrawn progress table). It never
  runs unattended.
- **PublishToolCli**: the non-interactive CLI for scripts and AI agents. Every input is an option, stored
  settings fill in the rest, nothing ever prompts or waits for a key; plain text output by default, one JSON
  result with `--json`, distinct exit codes. It has no Spectre dependency.

Both read and write the same config files and update themselves (each only itself) from GitHub releases
(`dalibor-osu/PublishTool`).

## Repository layout

```
PublishTool.slnx                  Solution (five projects)
Directory.Build.props             Repo-wide MSBuild props: references SonarAnalyzer.CSharp in every project
PublishTool.Core/                 Shared library (net11.0, IsAotCompatible, no Spectre); RootNamespace PublishTool
  BuildInfo (generated)           public BuildInfo.Version from -p:BuildVersion ("dev" otherwise), used by both exes
  Commands/ICommand.cs            ICommand / ICommand<TOptions> (ExecuteAsync only), consumed by the generator
  Attributes/                     CommandAttribute, OptionAttribute (consumed by the source generator)
  Progress/IStep.cs               The progress seam: IStepList.Add(name) -> IStep (Start, AppendLine, Finish, StepState)
  PublishWorkflow.cs              Build, publish and deploy preparation as steps; returns PublishResult; BuildConfigurations
  DeployPreparer.cs               The deploy preparation logic (download, compare, zip, upload, prune)
  ProjectScanner.cs               Finds *.csproj recursively, detects Sdk / LegacyWeb / LegacyLibrary, SelectByName
  DependencyBuild.cs              Writes a temp traversal .proj that restores + builds all projects with /m
  PublishCommandBuilder.cs        Builds the MSBuild command line per project kind
  MsBuildProperties.cs            PublishToolPrefix (property name) and PrefixProblem (which prefixes MSBuild can take)
  Configuration/                  Config file handling (no wizards)
    Config.cs                     Current config model (format version 2): Directories map of DirectorySettings;
                                  DirectorySettings.With(SettingsOverrides), MissingFor(local, projectsGiven), SettingKeys
    ConfigStore.cs                Load/save with version detection, migrator chain, backup of migrated files
    ConfigVersions.cs             Registry: Current version, parsers per version, migrators per step
    ConfigParser.cs / ConfigMigrator.cs / ConfigEnvelope.cs / IVersionedConfig.cs / ConfigException.cs
    JsonContext.cs                Source-generated JSON metadata for every readable config model and package file
    Versions/ConfigV1.cs          Frozen snapshot of format version 1 (five parallel dictionaries); never edit
    Migrations/ConfigV1ToV2.cs    Migrator 1 -> 2 (merges the dictionaries per directory)
    ServerConfig.cs               Server settings (own format version): Environments per prefix + Deployed records per path
    ServerConfigVersions.cs       Registry of the server settings file (parsers, migrators), separate from ConfigVersions
    DeployTargets.cs              Resolve(config, prefix, projects, overrides, skip): targets + missing + skipped, never mutates the config
  Deployment/                     Package manifest, lineage and everything deploy and rollback do
    PackageManifest.cs / ReferenceLineage.cs / PackageJson.cs / PackagePaths.cs / DeployPackage.cs / DeployJournal.cs
    PackageInventory.cs           publishtool-inventory.json: every file of the full publish per project (path, size, SHA-256)
    DeployPlan.cs                 Per target: files to create / overwrite / delete, coded warnings, errors; Clean / ResetClean
    TargetCleaner.cs              Walks one target for the clean: stray files, empty directories, mismatched and missing files
    KeepPatterns.cs               The keep patterns of a target (matching and validation)
    DeployWarning.cs              DeployWarning(Code, Message, Files) and WarningCodes (the stable codes --accept takes)
    ConfigFiles.cs                The fixed config file name patterns behind the config-changed warning
    DeployExecutor.cs             Backup, Apply and the idempotent Restore
    DeployRunner.cs               Deploy as steps incl. rollback on failure; returns DeployOutcome (Deployed/Failed/RestoreFailed)
    RollbackRunner.cs             Rollback as steps; CheckChain gives the newer-deploy warnings
    HostSet.cs / IHostController.cs / WindowsHostController.cs
  GitHub/                         GitHubClient (releases API, GetVersionAsync), ReleaseVersion (semver, FindAssetUrl),
                                  SelfUpdate (per executable: asset name, find, download, extract, swap exe, .old backup)
  Helpers/                        ProcessHelper, EnvironmentHelper, OptionParsers, FileHashing, WindowsAccount,
                                  CriticalSection (swallows Ctrl+C)
  Logger.cs                       In-memory log flushed to %APPDATA%\PublishTool\Logs\<date>.log; Write(report) says where
PublishTool/                      The TUI exe (PublishAot, InvariantGlobalization); RootNamespace PublishTool
  Program.cs                      Ctrl+C -> CancellationToken, removed-switch check, parse args, run command, write log
  RemovedSwitches.cs              Points users of the removed publish -a / deploy -y / rollback -y to PublishToolCli
  Commands/                       One class per command, [Command("name")] + ICommand<TOptions>; Options/ per command
    IAlternateScreenCommand.cs    Commands that run inside the alternate screen and wait for a key at the end
    PublishCommand.cs             Prompts for projects and configuration, runs PublishWorkflow on a JobDisplay
    DeployCommand.cs / RollbackCommand.cs   Summary, confirmation, runners on a JobDisplay (Windows, admin)
    ConfigCommand.cs              Forces the interactive config wizard for a directory
    UpdateCommand.cs / VersionCommand.cs / HelpCommand.cs
  Configuration/ConfigHandler.cs  Load(workingDir) = load file + wizard for missing settings, returns the DirectorySettings
  Configuration/ServerConfigHandler.cs   ResolveTargets: wizard for missing target paths / service names / skips of a prefix
  Console/JobDisplay.cs           JobRow (an IStep) / JobDisplay (an IStepList): the redrawn progress table
  Console/JobDisplayRunner.cs     Redraw loop for a JobDisplay while work runs
  Console/DeploySummary.cs        Summary table, warnings/errors and the accept/confirm prompt of deploy and rollback
  Console/CleanReview.cs          The clean question of deploy, its findings, the keep pattern prompt and the report file
  Console/BackKeyConsole.cs       IAnsiConsole wrapper whose input throws BackRequestedException on Esc / Left arrow
PublishToolCli/                   The CLI exe (PublishAot, InvariantGlobalization, no Spectre); RootNamespace PublishToolCli
  Program.cs                      Ctrl+C handling, parse args (no default command), run, log path to stderr
  ExitCodes.cs                    Exit code constants, status names, help text; CliArgumentException (exit 2)
  OptionValues.cs                 Shared option value parsers: lists, absolute paths, warning codes, project=value pairs
  Commands/                       publish, config, deploy, rollback, update, version, help; Options/ per command
  Output/CliOutput.cs             Text vs --json output, error lines, RunAsync (exception -> exit code, prints the result)
  Output/LineSteps.cs             IStepList printing one line per step start and finish, output tail only on failure
  Output/CliResults.cs            JSON result types per command + CliJsonContext (camelCase, schema version)
PublishTool.Generators/           Roslyn incremental source generator (netstandard2.0, Scriban templates)
  CommandParserGenerator.cs       Collects [Command] classes + [Option] properties, reads build_property.RootNamespace
  Emitter.cs + Templates/*.sbncs  Renders <RootNamespace>.Generated.CommandParsers (parsing + help text)
  Diagnostics.cs                  PT0001..PT0008 compile errors for bad command/option declarations
PublishTool.Tests/                xUnit tests of the core and the CLI parsing (temp directories, FakeHostController)
.github/workflows/release.yml     Manual release: semver from Conventional Commits, AOT build of both exes for 4 RIDs
TestApps/                         Four sample apps (Core/Framework x IIS/service) + scripts for an end-to-end deploy test
docs/specs/                       Agreed design specifications
docs/impl/                        Implementation notes per spec
skills/publishtool/SKILL.md       Claude skill for users of PublishToolCli (workflow and safety rules, not option syntax)
```

`skills/publishtool/SKILL.md` teaches an AI agent in another repository how to install and use PublishToolCli; users
copy it into their own skills directory (see `README.md`). It leaves the option syntax to `--help` on purpose, but it
describes the behavior an agent relies on: how to install and update the tool (release asset names, archive layout),
the publish, deploy and rollback workflows, which commands change something, the exit codes, every warning code and
what it means, and the JSON result fields it tells the agent to read. **Whenever a change alters any of that behavior
(a new or changed command, option that widens what a run does, warning code, exit code, result field, journal state,
release naming, requirement), update the skill in the same change**, exactly like this file. A skill that describes
old behavior is a bug, because agents act on servers from it.

## Tests

`PublishTool.Tests` (xUnit, references `PublishTool.Core` and `PublishToolCli`) runs from the IDE test runner or with
`dotnet test PublishTool.Tests`. It covers path escaping, the `PublishToolPrefix` on every MSBuild command line and
which prefixes are accepted, config migration, deploy planning and its warning codes (including `config-changed`
and `target-modified` with their file lists),
backup/apply/restore, the deploy and rollback runners (with `FakeHostController` and a sandboxed
`ConfigStore<ServerConfig>`, no administrator rights), change collection and lineage, skipped projects (resolution,
filtered verification and plan), target and settings overrides
(including that overrides never change the stored config), the inventory (every published file, reused hashes,
carried projects), keep patterns, the clean (stray, kept, protected, mismatched and missing files, nested targets,
hidden files and junctions, projects that are not in the package, deploy and rollback of a clean), self-update asset
names and the CLI option parsing.
Tests never use `ConfigStore.Default` or `ConfigStore.Server`, which point at the real config of the machine.

Not covered: the TUI (prompts, alternate screen), MSBuild publishing and real IIS/services. `publish` of the TUI needs
a real terminal. The CLI can be exercised end to end against a throwaway git repository with only per-run overrides
(see `docs/impl/2026-11-24-non-interactive-cli.md`), and `TestApps/README.md` has the elevated end-to-end test with
real IIS sites and services.

## Development commands

Requirements: .NET SDK 11.x (`dotnet --version`), and MSBuild on `PATH` to actually run `publish`
(Visual Studio / Build Tools; the tool calls `MSBuild` by name, not `dotnet build`).

```powershell
# Build everything (Debug produces PublishTool-dev.exe and PublishToolCli-dev.exe, version "dev")
dotnet build PublishTool.slnx -c Debug

# Full static analysis pass: forces a recompile so every Sonar (S####) and compiler warning is reported
dotnet build PublishTool.slnx -c Debug --no-incremental

# Tests
dotnet test PublishTool.Tests

# Run the dev binaries
PublishTool\bin\Debug\net11.0\PublishTool-dev.exe --help
PublishTool\bin\Debug\net11.0\PublishTool-dev.exe publish -dir C:\path\to\repo
PublishTool\bin\Debug\net11.0\PublishTool-dev.exe deploy --dry-run -dir C:\path\to\deploy\uat_2026-01-01_12-00-00
PublishToolCli\bin\Debug\net11.0\PublishToolCli-dev.exe --help
PublishToolCli\bin\Debug\net11.0\PublishToolCli-dev.exe publish -dir C:\path\to\repo --projects Web,Api --json
PublishToolCli\bin\Debug\net11.0\PublishToolCli-dev.exe deploy -dir C:\path\to\deploy\uat_2026-01-01_12-00-00

# Native AOT publish like CI does (needs the VC++ toolchain on Windows); AOT/trim warnings only show up here
dotnet publish PublishTool/PublishTool.csproj -c Release -r win-x64 --self-contained -p:PublishAot=true -p:BuildVersion=1.2.3 -o artifacts/win-x64/PublishTool
dotnet publish PublishToolCli/PublishToolCli.csproj -c Release -r win-x64 --self-contained -p:PublishAot=true -p:BuildVersion=1.2.3 -o artifacts/win-x64/PublishToolCli

# Inspect what the source generators emitted (EmitCompilerGeneratedFiles is on in both exes)
Get-ChildItem PublishTool/obj/generated, PublishToolCli/obj/generated -Recurse -Filter *.g.cs
```

`dotnet build` of the solution also rebuilds the generator; if generated code looks stale, delete
`PublishTool/obj` and `PublishToolCli/obj` and build again.

### Releasing

Releases are cut manually with the **Release** GitHub Actions workflow (`workflow_dispatch`). It derives the
next version from Conventional Commits since the last tag (or takes an explicit bump/version), builds both
executables with Native AOT for win-x64/arm64 and linux-x64/arm64 (osx builds are disabled), packages them, tags
`v<version>` and creates one GitHub release with all assets (+ `SHA256SUMS.txt`):

- TUI: `PublishTool-v<version>-<rid>.zip|tar.gz`
- CLI: `PublishToolCli-<rid>-v<version>.zip|tar.gz`

The CLI name puts the RID before the tag on purpose: PublishTool versions released before the CLI pick the first
asset whose name ends in `<os>-<arch>.<ext>` and fall back to the only non-LICENSE file in it, so a CLI asset ending
that way could replace their `PublishTool.exe` with the CLI. Current versions match their exact asset name
(`SelfUpdate.Tui` / `SelfUpdate.Cli`) and look for exactly their own executable in the archive. Keep the naming in
`release.yml` and `SelfUpdate` in sync. Use Conventional Commit messages (`feat:`, `fix:`, `feat!:`/`BREAKING CHANGE`)
so `auto` bump works.

## Architecture notes and conventions

- Always add newly created files to git (git add)
- **Native AOT**: no reflection-based serialization or dynamic code. JSON goes through `JsonContext` (core) and
  `CliJsonContext` (CLI results), command parsing is source generated, regexes use `[GeneratedRegex]`. The core has
  `IsAotCompatible`, so trim/AOT analyzers run on it; run the AOT publish of both exes after touching it.
- **Where code goes**: anything that is not presentation lives in `PublishTool.Core` and never prompts or writes to
  the console. Choices (projects, configuration, target mappings, accepting warnings) are parameters or return
  values of the core; the TUI fills them from prompts and wizards, the CLI from options and stored settings.
  Executable names in core messages are passed in (`DeployRunner` takes the name used in "run '<exe> rollback'").
- **Progress seam**: long-running core work (`PublishWorkflow`, `DeployRunner`, `RollbackRunner`) creates its steps
  up front from an `IStepList` and reports through `IStep` (`Start`, `AppendLine`, `Name` may change while running,
  `Finish(StepState, detail)`). The TUI passes a `JobDisplay` and redraws it with `JobDisplayRunner`; the CLI passes
  `LineSteps`, which prints `[start] <step>` and `[done|failed|skipped|cancelled] <step>: <detail> (<seconds>s)` and
  shows a failed step's error lines (or last lines); `--verbose` prints every line.
- **Adding a command**: create `Commands/FooCommand.cs` in the executable with `[Command("foo", Description = "...")]`
  implementing `ICommand<FooCommandOptions>` (from the core), and `Commands/Options/FooCommandOptions.cs` with
  `[Option]` properties. Supported option types are `bool` (flag), `string`, `int` (also nullable), or any type via
  `Parser = nameof(StaticMethod)` with signature `static bool M(string value, TOptions options, out string? error)`
  (a parser may add to a collection, which makes the option repeatable, e.g. `--target`). Only properties declared on
  the options class itself are seen, so shared options like `--json` are declared per class. `-h/--help` is reserved
  and added to every command. `IsDefault = true` marks the command that runs when none is given (`publish` in the
  TUI; the CLI has none and prints help with exit 2). The generator emits compile errors PT0001-PT0008 for mistakes.
- **One generator, two executables**: the generator reads `build_property.RootNamespace` (each exe has
  `<CompilerVisibleProperty Include="RootNamespace"/>`) and uses it as the application name in help, for the help
  command type `<RootNamespace>.Commands.HelpCommand` and for the generated namespace `<RootNamespace>.Generated`.
  The attribute, `ICommand` and `Result` type names it emits are fixed (`PublishTool.*` in the core).
- **TUI UI model**: commands implementing `IAlternateScreenCommand` with `UsesAlternateScreen => true` (publish,
  config, deploy, rollback) run inside Spectre's alternate screen and wait for a key press at the end. The commands do
  not use Spectre live widgets; `JobDisplayRunner` redraws a `JobDisplay` every 100 ms while the work task runs. The TUI
  is interactive only: `publish -a`, `deploy -y` and `rollback -y` were removed, and `RemovedSwitches` prints a pointer
  to PublishToolCli (exit 1) when someone still passes them.
- **CLI output contract**: every command builds a `CliResult` subtype and runs its body through
  `CliOutput.RunAsync`, which maps exceptions to exit codes (`CliArgumentException`/`ConfigException` -> 2,
  `DeployException` -> 1, cancellation -> 130), sets `status`/`exitCode` and, with `--json`, prints exactly one JSON
  object on stdout (a command line that cannot be parsed still prints a minimal result from `Program.cs` when
  `--json` is among the arguments). A success code is never turned into 130 afterwards: commands return 130 themselves
  when they were cancelled. Text mode writes progress and results to stdout and `error: ...` lines to stderr; `--json` moves
  every progress line to stderr, and the log-file notice of `Logger.Write` always goes to stderr. The JSON carries
  `schemaVersion` (`CliResult.CurrentSchemaVersion`, independent of the tool version): within one schema version
  changes are additive only; renaming or removing a field means a new schema version. A warning about files
  (`config-changed`, `target-modified`, `clean-delete`, `clean-mismatch`, `clean-missing`) names the first
  `DeployWarning.MaxListedFiles` in its message and carries the complete list in its `files` field; other warnings
  have no `files`. In the `files` of `clean-delete` a path ending in `/` is an empty directory that is removed.
- **CLI exit codes** (`ExitCodes`): 0 success (a dry run whose package and plan are valid exits 0 whatever warnings
  it lists; verification or plan errors are 1, missing targets 2), 1 failed, 2 invalid arguments or missing
  configuration, 3 stopped on warnings not accepted with `--accept`, 4 a deploy failed and its automatic restore failed
  too (journal `RestoreFailed`, run `rollback`), 130 cancelled. Missing administrator rights for `--yes` is exit 1.
  The TUI keeps 0/1.
- **Warning codes** (`WarningCodes`): `tool-version`, `target-missing`, `already-deployed`, `full-publish`,
  `unknown-base`, `no-record`, `chain-mismatch`, `config-changed`, `target-modified` (deploy), `clean-delete`,
  `clean-mismatch`, `clean-missing` (deploy with the clean), `newer-deploy` (rollback), `downgrade` (CLI update).
  They are
  part of the CLI's public contract: never rename one, and give a new check a new code so existing scripts never
  accept it by accident.
- **Going back in prompts** (TUI): Spectre prompts have no back key, so a prompt that should support it is shown on
  a `BackKeyConsole` wrapping `AnsiConsole.Console`; its input throws `BackRequestedException` on Esc or
  Left arrow (see `PublishCommand.PromptConfiguration`). Spectre does not clean up a prompt stopped this way
  (its lines stay and the cursor stays hidden), so the catch calls `AnsiConsole.Clear()` and
  `AnsiConsole.Cursor.Show()`. Don't use it on prompts where Left arrow means something (hierarchical
  multi-selection, text input).
- **Cancellation**: Ctrl+C cancels the shared `CancellationTokenSource`; MSBuild processes are killed with
  their process tree. Every loop over files must call `ct.ThrowIfCancellationRequested()`. Cleanup of temp
  directories happens in `finally` blocks in `PublishWorkflow.PrepareDeploy`. While a `CriticalSection` is
  entered (deploy from stopping the hosts to the end of a restore, the whole rollback run), both `Program.cs` files
  swallow every further Ctrl+C instead of letting the second one kill the process; the first one still cancels the
  token, which a deploy treats as a failure and rolls back.
- **Errors**: user-facing messages are short; full exceptions go to `Logger.LogError`. The log is written to
  `%APPDATA%\PublishTool\Logs\yyyy-MM-dd.log` only if something was logged, and the path is printed (stdout in the
  TUI, stderr in the CLI).
- **Process output**: use `ProcessHelper.RunAsync`; it streams stdout/stderr lines to a callback and returns
  exit code plus captured output.
- **Config**: `%APPDATA%\PublishTool\config.json`, shared by both executables: `Version` plus `Directories`, a map
  from the absolute working directory to its `DirectorySettings` (`PublishDirectory`, `SharedDirectory`,
  `DeployDirectory`, `IgnoredDirectories`, `PublishableProjects`). A setting that is `null`/absent was never set.
  The TUI's `ConfigHandler.Load(workingDir)` runs the wizard for it, saves, and returns the directory's
  `DirectorySettings`. The CLI never runs a wizard: `publish` merges per-run options over the stored settings with
  `DirectorySettings.With(SettingsOverrides)` (never saved) and fails with exit 2 naming the missing keys from
  `MissingFor` (`publish-dir` always, `shared-dir`/`deploy-dir` unless `--local`, `publishable` unless `--projects`;
  `ignore` defaults to `.git, .claude`). Only `PublishToolCli config` (or the TUI wizard) changes the file. The CLI
  option names equal `SettingKeys`. `DirectorySettings.IsComplete` (annotated with `MemberNotNullWhen`) tells TUI
  consumers everything is set; `ProjectScanner.Scan` takes the `DirectorySettings?` directly. Adding a new
  per-directory setting means adding a nullable property to `DirectorySettings`, including it in `IsComplete`,
  `With`, `MissingFor` (if required), `SettingKeys`, prompting for it in `ConfigHandler.RunWizard` and adding the
  CLI options to `publish` and `config`. That is **not** a new format version.
- **Server config**: `%ProgramData%\PublishTool\server.json` (machine-wide, written by elevated deploys),
  model `ServerConfig`, own registry `ServerConfigVersions` and store `ConfigStore.Server`, same versioning
  machinery and recipe as `config.json` but its own version number. `Environments` maps a package prefix to
  `Targets` (project name -> `Path`, optional `ServiceName`; no service name means an IIS app) and `Skipped`
  (projects of the prefix that are not deployed on this server, because one publish feeds several servers);
  `Deployed` maps an absolute target path to the last package deployed there. Lookups are case-insensitive
  (`ServerConfig.FindKey`). A project is either mapped or skipped: `DeployEnvironment.Skip` removes its target,
  setting a target un-skips it. `Skipped` was added without a format version bump (older files read it as empty),
  but an older PublishTool that saves `server.json` drops it. `DeployTargets.Resolve` merges per-run
  `TargetOverride`s (CLI `--target`/`--service`) and per-run skips (CLI `deploy --skip`) over the stored targets
  into new objects and reports projects without a path (`Missing`) and skipped ones (`Skipped`; a per-run target
  path un-skips a stored skip for that run); it never adds an environment or a target to the config, so a deploy
  with overrides only writes its `Deployed` records and journal. Missing targets are asked for by the TUI's
  `ServerConfigHandler.ResolveTargets` (first "is it deployed on this server?", then absolute path and service,
  which must exist on the machine) and stored; the CLI fails with exit 2 instead. `PublishToolCli config --prefix <p>`
  shows, sets, changes and removes targets and skips (`--target`, `--service`, `--remove-target`, `--skip`,
  `--remove-skip`); writing `server.json` usually needs an elevated terminal. A target also has `Keep`, the patterns
  of files and directories the clean leaves alone (added like `Skipped`, without a format version bump, so an older
  PublishTool that saves `server.json` drops them). Every place that rebuilds a `DeployTarget` from a stored one
  (`DeployTargets.Resolve`, the CLI's `ConfigCommand.Merge`) must copy `Keep`, otherwise changing a path silently
  removes the protection of logs and uploads. Pattern rules (`KeepPatterns`): `/` or `\` separates segments, `*` and
  `?` match inside one segment, `**` matches any number of segments, case-insensitive; a pattern with a separator is
  relative to the target directory (`logs/**`, `wwwroot/uploads`), one without matches a name at any depth (`*.log`,
  `App_Data`); whatever matches a directory keeps everything below it and the directory itself. Rooted patterns and
  `.`/`..` segments are rejected. The CLI sets them with `config --prefix <p> --keep <project=pattern>` /
  `--remove-keep` and adds per-run ones with `deploy --keep`; the TUI asks for them in the clean review.
- **Config format versions**: the file carries `Version` (files without it are version 1). `ConfigStore`
  reads the version, parses with that version's `ConfigParser`, runs the `ConfigMigrator` chain step by step
  to `ConfigVersions.Current`, then backs the old file up as `config.v<N>.json.bak` and saves the upgraded
  one. A file newer than the tool is rejected with a `ConfigException` (shown in red by the TUI, exit 2 in the CLI);
  there is no downgrade. Both executables share one version number, so a matching pair always agrees on the format.
  Bump the version only when the old model can no longer read the file (renamed/reshaped properties). Version 2 is
  the worked example: version 1 stored five parallel dictionaries (`Versions/ConfigV1.cs`), `Migrations/ConfigV1ToV2.cs`
  merges them per directory and leaves settings a directory never had as `null`. The store validates at startup that
  every version has a parser and every step a migrator, so registry mistakes fail on the first run. Recipe for a new
  version N:
  1. Copy the current `Config` (and `DirectorySettings`) to `Versions/ConfigV<N-1>.cs` as a frozen snapshot:
     plain classes, no `IVersionedConfig`, `Version` as a plain int, no helpers.
  2. Change `Config` to the new shape, set `ConfigVersions.Current = N`, fix the wizard, the CLI and the consumers.
  3. Register the snapshot in `JsonContext`, add `new ConfigParser<ConfigV<N-1>>(N-1, JsonContext.Default.ConfigV<N-1>)`
     to `ConfigVersions.Parsers`, and change the current model's parser entry to version N.
  4. Add `Migrations/ConfigV<N-1>ToV<N>.cs` deriving from `ConfigMigrator<ConfigV<N-1>, Config>` and add it to
     `ConfigVersions.Migrators`. Older migrators keep targeting their snapshot (`ConfigMigrator<ConfigV1, ConfigV2>`
     once version 3 exists), so migration always runs one step at a time.
  5. Add a test to `PublishTool.Tests/Configuration/ConfigStoreTests.cs` that feeds a real old file through a
     `ConfigStore<Config>` pointed at a temp path.
- **Project kinds** (`ProjectScanner.DetectKind`): SDK-style projects are published with
  `/t:Publish /p:RuntimeIdentifier=win-x64 /p:SelfContained=false`; legacy web projects
  (`Microsoft.WebApplication.targets`) use `DeployOnBuild` + `WebPublishMethod=FileSystem`; other legacy
  projects just build with `OutDir`. Project references are built once up front, then each publish runs with
  `/p:BuildProjectReferences=false`. `<publish dir>\<Project>` is deleted before each publish, because
  `dotnet publish` never removes stale files and deleted files would otherwise never show up as deletions.
  SDK projects embed the git commit in their assemblies, so their dll/exe/pdb change with every commit.
  The TUI offers the publishable projects of the directory config; the CLI publishes them by default, or the
  projects named with `--projects`, looked up by exact name (case-insensitive) among all projects in the directory
  (`ProjectScanner.SelectByName` rejects unknown and ambiguous names).
- **The prefix property** (`MsBuildProperties`): the package prefix is resolved once per publish run, before the build
  (also with `--local`), and passed as `PublishToolPrefix=<prefix>` to the traversal project's restore and build and
  to every publish, for all project kinds; the same value names the package and goes into the manifest. It is the
  sanitized branch, or `DEPLOY` outside git, and is always passed, so a consumer treats unknown values as "no
  environment". The tool knows nothing about environments: mapping a prefix to configs is the published repository's
  business (e.g. its `Directory.Build.props`). A prefix with any character but letters, digits, `.`, `_`, `-` and `+`
  (`;`, `,`, `=`, `$`, `%`, ... are special to MSBuild) fails the build step before anything runs, instead of being
  escaped. The property name is public contract: never rename it.
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
  0 warnings, and run the tests.** Fix findings rather than silencing them. For a real false positive, suppress as
  narrowly as possible (`[SuppressMessage]` with a `Justification` on the member, see `Emitter.cs`); a rule that does
  not fit this project at all is disabled in the Sonar section of `.editorconfig` with a comment saying why.

## The deploy workflow (DeployPreparer)

Runs after every publish unless `-l/--local` is given, the same way in both executables (`PublishWorkflow`).

Naming: `<prefix>_<yyyy-MM-dd_HH-mm-ss>` where prefix is the sanitized current git branch (`DEPLOY` if
not in a git repo), the same value the build got as `PublishToolPrefix`. The shared directory contains **only zip files** named `<name>.zip`, project
directories at the archive root (`Web/...`, `Api/...`), created with `CompressionLevel.Fastest`.

Steps, each its own step in the UI (five deploy steps in total):

1. **Download**: `FindPreviousReference(prefix)` picks the newest `<prefix>_*.zip`. It is copied to
   `<deploy dir>\<name>.tmp\previous.zip` and extracted to `<deploy dir>\<name>.tmp\previous\`. If a
   directory (old workflow) with the prefix is newer than the newest zip, the run fails and tells the user to
   zip or delete it, because ignoring it would produce an "everything changed" deploy.
2. **Collect changes**: every file of every published project is compared byte-wise with the extracted
   reference; changed or new files are copied to `<deploy dir>\<name>\<Project>\...` and listed in the
   manifest with size and SHA-256, plus `PreviousSha256`, the hash of the file in the reference it replaces
   (absent for a file that is new in this publish). Files in the reference but missing from the new publish are
   listed as deletions with the SHA-256 of the deleted file (case-insensitive, so a case-only rename is never a
   deletion). These hashes let `deploy` see files that were changed on the server. The manifest format is still
   1: no package was deployed with the earlier shape, so it changed in place; from now on a shape change means a
   new format. A project with no changes and no
   deletions is left out of the manifest. Each project's `Base` is the package that last published it,
   read from the reference's lineage; `IsFullPublish` is set when the reference did not contain the project.
   The manifest is written to the `.tmp` work directory here. The same pass builds the inventory
   (`PackageInventory`, `publishtool-inventory.json`): every file of every published project with size and
   SHA-256, also for projects without changes, plus the projects carried over from the reference. It costs no
   extra hashing after the first publish of a branch: a changed file is hashed for the manifest anyway, an
   unchanged one takes its hash from the inventory inside the previous reference, and only files that inventory
   does not know are hashed. The inventory is written into the package directory right away (it means nothing
   without the manifest) and is always written; there is no switch for it.
3. **Compress**: the full publish is zipped to `<deploy dir>\<name>.tmp\<name>.zip`. Projects that exist in
   the previous reference but were not published this time are carried over from the extracted reference,
   so the reference stays complete for partial publishes. A legacy `Deploy` subdirectory is skipped. The zip
   root gets `publishtool-lineage.json`: project -> the package that last changed it (this package for
   changed projects, the previous value for unchanged and carried projects) and `publishtool-inventory.json`,
   the same inventory the package gets, which the next publish reads its hashes from.
4. **Upload**: the zip is streamed to `<shared>\<name>.zip.uploading` with percent progress, then renamed
   to `<name>.zip`. Only then is the manifest moved to `<deploy dir>\<name>\publishtool-manifest.json`
   (if that fails, the new reference is deleted again), so a package without a committed reference never
   has a manifest and `deploy` refuses it. The `.tmp` work directory is deleted; on failure or cancel the
   partial upload and the deploy directory are deleted as well.
5. **Prune**: after the new zip is in place, all but the newest `KeepReferences` (3, constant in
   `PublishWorkflow`) archives of the same prefix are deleted from the share. Only `.zip` files whose name
   parses as `<prefix>_<timestamp>` are touched; old-workflow directories, other branches and `.uploading`
   leftovers are ignored. A failed delete is logged and shown but does not fail the publish.

The result (`PublishResult`: prefix, package name and directory, new and previous reference, per-project change counts,
carried projects, pruned archives) is what the CLI prints and puts into its JSON.

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

Windows only (other platforms exit 1 at once). Changing the server needs administrator rights; a dry run does not.
Everything is driven by the manifest: a directory or file not listed there is never deployed (extra files are
shown in the summary), and every manifest path goes through `PackagePaths.Resolve`, which rejects paths
outside the package or target.

`deploy -dir <package>` preflight, nothing changes on the server: admin check (unless a dry run), refuse if
`<package>\.backup` exists (deployed before or interrupted, run `rollback` first; not checked in a dry run), load the
manifest (missing or newer format is refused), resolve the targets of the manifest's prefix, leave the skipped
projects out (`DeployPackage.WithoutProjects`: a filtered manifest copy, so verification, plan, journal and records
never see them, and their directories, copied or not, are neither verified nor reported as extra files), verify
every remaining file's size and SHA-256, build the `DeployPlan`. Skipped projects are listed in the summary; when
every project is skipped (`DeployPlan.IsAllSkipped`) there is nothing to deploy and it exits 0. Errors stop it: two projects mapped to one path, a missing service, IIS
targets without IIS. Warnings carry codes (see `WarningCodes`): the package was made by another PublishTool version
(exact string match with `BuildInfo.Version`, `tool-version`), a target directory does not exist (`target-missing`),
and the chain check per target: the target's `Deployed` record must equal the project's `Base`; an unknown base
(`unknown-base`), a missing record (`no-record`), a mismatch, i.e. a skipped or out-of-order package
(`chain-mismatch`), the package already deployed there (`already-deployed`), or a full publish onto a deployed target
(`full-publish`) all warn. Two file checks follow per target, one warning each with the files attached:

- `config-changed`: the plan overwrites or deletes config files (`ConfigFiles`: `web.config`, `*.exe.config`,
  `*.dll.config`, `appsettings.json`, `appsettings.*.json`, by file name, case-insensitive). Config files the
  package creates on the target do not warn.
- `target-modified`: every file the plan overwrites is hashed on the target and must equal the manifest's
  `PreviousSha256` or the new `Sha256` (a re-deploy); a file without `PreviousSha256` that already exists with other
  content is reported too. Every file the plan deletes must equal the hash recorded for the deletion. A file that
  cannot be read is reported as "(could not be read)", never treated as unmodified. The check also runs when the
  chain check warns or for a full publish, where most existing files differ (it lists them next to the chain warning
  that explains why). Hand edits to files the package does not contain stay invisible.

Both checks hash in the preflight, before the backup, also in a dry run.

- **TUI** (`PublishTool deploy`): asks for missing targets (or whether the project is deployed on this server at all)
  in a wizard and stores them, shows the summary, asks to accept warnings (default no) or to confirm; `--dry-run`
  stops after the summary.
- **CLI** (`PublishToolCli deploy`): without `--yes` (or with `--dry-run`) it is a dry run: verify, print the plan and
  the warnings (with `accepted` flags), exit 0. Missing targets are exit 2 (pass `--target <project>=<path>` and
  `--service <project>=<name>`, or `--skip <project>`, for this run, or store them with `config --prefix`); `--skip`
  of a project the package does not contain, or together with `--target`/`--service` for the same project, is exit 2.
  The JSON lists skipped projects in `skipped`. With `--yes`: an empty plan exits 0, any warning whose code is not
  in `--accept` stops with exit 3 before anything changes, otherwise it runs.

`DeployRunner` steps: backup (overwritten and deleted files into `<package>\.backup\<Project>\...`, journal
`journal.json` written before and after, state `BackingUp`), stop (journal state `Applying` with the hosts that
were running and will be started again; services first, then `iisreset /stop` if any target has no service
name), apply (copy, delete, remove directories the deletions left empty), start (`iisreset /start`, then the
services; only what was running before), record (`Deployed` records + journal state `Deployed`). Any failure
or Ctrl+C after the backup restores the backup (`DeployExecutor.Restore`: copy backed-up files back, delete
created files and empty created directories; idempotent, so it does not need to know how far apply got),
resets the records if they were written, starts the hosts again and removes the backup. Before restoring after
the apply started, it stops the hosts again (a failed start may have started some of them, and running apps
lock their files); if the failure came before apply, nothing was changed, so it skips the restore. If the
restore itself fails, the journal becomes `RestoreFailed`, the backup stays, the outcome is
`DeployOutcome.RestoreFailed` (CLI exit 4) and the user runs `rollback`. A failed or cancelled backup removes only a
backup this run created.

`rollback -dir <package>` reads the journal: `BackingUp` means nothing on the server changed, so it only removes
the backup (the CLI only with `--yes`). Otherwise it warns (`newer-deploy`) when a target's record is not this
package (a newer deploy happened; skipped for `Applying`, an interrupted deploy, and `RestoreFailed`), stops the
running hosts, restores, starts what it stopped plus, for `Applying` and `RestoreFailed`, what the deploy had
stopped, resets the records to the journal's previous ones and deletes the backup. The TUI asks for confirmation;
the CLI is a dry run without `--yes` and stops with exit 3 on a warning not accepted with `--accept newer-deploy`.
Backups are otherwise never pruned; deleting a package directory deletes its backup.

### The clean

`DeployPlan.Clean(inventory, overrides, skip, ct)` runs on a plan that already exists, in the preflight, so a dry run
shows it and nothing is deleted before the backup. It needs `publishtool-inventory.json` in the package
(`DeployPackage.LoadInventory` refuses a missing one and one whose package name is not the manifest's). The
inventory is the complete state a target should have after the package, so the clean does not depend on the
chain check. It covers every project of the inventory that has a target on this server and is not skipped, also
projects that are not in the manifest (published without changes, or carried over): such a project becomes a
clean-only `TargetPlan` (`IsCleanOnly`, journal `CleanOnly`) when there is something to delete, its hosts are stopped
like any other, but it gets no `Deployed` record, is left out when records are reset and is ignored by the
`newer-deploy` check. Inventory projects without a target are never asked for and never an error; they are listed
as not cleaned.

`TargetCleaner` walks the target itself (hidden and system files included) and sorts what it finds:

- kept: whatever matches a keep pattern of the target (stored `Keep` plus per-run `--keep`) is neither deleted nor
  compared nor reported as missing;
- never entered: reparse points (junctions, symlinks), directories that cannot be listed, and every other target
  path of `server.json` (all prefixes), of this run and the package directory, so a target nested in another one
  (an IIS sub-application) is not deleted with its parent;
- stray: a file that is not in the inventory is deleted, unless the package already deletes it;
- protected: a stray config file (`ConfigFiles`) is never deleted, only listed (`DeployPlan.ProtectedConfigFiles`,
  not a warning);
- mismatched: a file of the inventory that the package does not contain and whose size or SHA-256 differs is
  reported, as is one that cannot be read; the package holds only changes, so it cannot repair them;
- missing: a file of the inventory that is neither on the target nor in the package is reported;
- empty directories: a directory that is empty after the clean and does not belong to the publish is removed,
  also one that was empty before.

Each target gets up to three warnings with the files attached: `clean-delete` (stray files and empty directories),
`clean-mismatch` and `clean-missing`. Stray files are backed up and journaled like deleted files (`BackedUp`), the
removed directories go into the journal's `RemovedDirectories`, so the automatic restore and `rollback` bring both
back. Backup copies lose a read-only attribute so the backup can always be removed, and a read-only stray file is
made writable before it is deleted. At apply time a directory is removed only if it is still empty.
`ResetClean` undoes a computed clean on the plan; `Clean` starts with it, so it can run again after a keep pattern
was added.

- **CLI**: `deploy --clean` (plus `--keep <project=pattern>` for this run). The dry run lists everything; with
  `--yes` a clean warning that is not in `--accept` stops with exit 3 before anything else, also when the package
  itself has nothing to deploy. `--keep` without `--clean`, or for a project the package does not have, is exit 2;
  a package without an inventory is exit 1. The JSON has `clean`, `notCleaned`, `protectedConfigFiles` and per target
  `cleanOnly`, `cleanFiles`, `cleanDirectories`.
- **TUI**: there is no switch. When the package has an inventory, deploy asks after the last confirmation whether to
  clean (default no). A yes computes the clean, shows the findings and writes the complete lists to
  `<package>\publishtool-clean.txt`, then offers: accept and continue, add a keep pattern for a project (stored in
  `server.json`, the clean is computed again), continue without cleaning, or cancel. A package without changes
  still gets the question, so a server can be cleaned with it.

## Usage guide (end user)

```
PublishTool [command] [options]             interactive TUI
  publish  (default)  -dir <path>  -l|--local
  deploy              -dir <package dir>  --dry-run                       (server, elevated)
  rollback            -dir <package dir>                                  (server, elevated)
  config              -dir <path>
  update              -pre|--pre-release  --fetch  -v|--version <version>
  version
  -h, --help          on every command

PublishToolCli <command> [options]           non-interactive CLI, never prompts
  publish   -dir <path>  -p|--projects <a,b>  -c|--configuration Release|Debug  -l|--local
            --publish-dir <path>  --shared-dir <path>  --deploy-dir <path>  --ignore <dirs>  --json  --verbose
  deploy    -dir <package dir>  -y|--yes  --dry-run  --accept <codes>
            --target <project=path>...  --service <project=name>...  --skip <projects>...
            --clean  --keep <project=pattern>...
            --json  --verbose                                                    (server; --yes needs elevation)
  rollback  -dir <package dir>  -y|--yes  --accept newer-deploy  --json  --verbose   (server; --yes needs elevation)
  config    -dir <path>  [--publish-dir|--shared-dir|--deploy-dir <path>]  [--ignore|--publishable <list>]
            [--unset <keys>]  --json
  config    --prefix <prefix>  [--target <project=path>]...  [--service <project=name>]...
            [--remove-target <project>]...  [--skip <projects>]...  [--remove-skip <projects>]...
            [--keep <project=pattern>]...  [--remove-keep <project=pattern>]...  --json
  update    -pre|--pre-release  --fetch  -v|--version <version>  --accept downgrade  --json
  version   --json
  -h, --help  on every command; PublishToolCli --help also lists the exit and warning codes
```

1. **First run in a repository**: `PublishTool -dir C:\repo` (or run it from the repo). The wizard asks for
   the publish directory (local output, one subdirectory per project), the shared directory (network share
   holding the reference zips), the deploy directory (local, receives `<name>` change sets), directories to
   ignore while scanning (default `.git, .claude`), and which projects are publishable. Re-run the wizard
   with `PublishTool config -dir C:\repo`. Without the TUI: `PublishToolCli config -dir C:\repo --publish-dir ...
   --shared-dir ... --deploy-dir ... --publishable Web,Api`.
2. **Publish**: `PublishTool publish` prompts for projects and configuration (Release/Debug). Pressing Esc or
   Left arrow in the configuration prompt reopens the project prompt with the previous selection preselected.
   Then it shows the build row, one row per project and the five deploy rows. `-l/--local` only publishes locally
   and skips the deploy rows. Unattended: `PublishToolCli publish -dir C:\repo` publishes every publishable project
   in Release (`--projects`, `--configuration` and the directory options override), prints one line per step and the
   package path, or with `--json` one result object (`packageName`, `packageDirectory`, `changes`, ...). Exit code 0
   on success.
3. **Deploy**: after a publish without `--local`, copy `<deploy dir>\<branch>_<timestamp>` to the server; it
   contains only the files that differ from the previous publish of the same branch plus the manifest. On the
   server, in an elevated terminal, run `PublishTool deploy -dir <package>`. The first package of a prefix asks
   for each project whether it is deployed on this server, and if so for its target directory and Windows service
   name (empty for IIS apps), stored in `%ProgramData%\PublishTool\server.json`. When the projects of one publish
   live on several servers, copy only the project directories a server needs plus the manifest; the projects
   answered "no" are left out there (CLI: `PublishToolCli config --prefix <p> --skip Api,Worker`, undo with
   `--remove-skip` or by setting a `--target`). Review the summary and warnings, confirm, and watch the rows.
   Unattended: `PublishToolCli deploy -dir <package>` (dry run, review the warnings), then
   `PublishToolCli deploy -dir <package> --yes --accept <codes you reviewed>`. Deploy packages of a branch in order;
   the tool warns when one was skipped. Config files are deployed like any other file, so the app itself must ship
   correct per-environment configs (it can pick them from the `PublishToolPrefix` MSBuild property). Deploy warns
   with `config-changed` when it overwrites or deletes a config file and with `target-modified` when a file on the
   server is not what the package expects to replace; `PublishToolCli deploy --dry-run --json` lists all files. Keep the package directory: it holds the backup. `rollback -dir <package>`
   (with `--yes` in the CLI) undoes the deploy.
4. **Clean a server**: target directories collect files that no publish contains any more (from before the tool
   deleted files, from skipped packages, from manual copies). Copy `publishtool-inventory.json` to the server together
   with the manifest. First tell the tool what lives only on the server:
   `PublishToolCli config --prefix <p> --keep Web=logs/** --keep Web=App_Data` (or add the patterns in the TUI when it
   shows what it would delete). Then `PublishToolCli deploy -dir <package> --clean` shows the files it would delete,
   the config files it leaves alone and the files that differ from the publish or are missing, and
   `PublishToolCli deploy -dir <package> --clean --yes --accept clean-delete` (plus `clean-mismatch` / `clean-missing`
   when you reviewed those) deploys and cleans in one run. In the TUI, `PublishTool deploy` asks about the clean after
   the confirmation. Files that differ or are missing are only reported: a package holds changes, not the whole
   publish. `rollback` restores the deleted files too.
5. **Update the tool**: `PublishTool update` / `PublishToolCli update` (or `update --fetch` to only check); each
   replaces only its own executable. Dev builds cannot update. The previous executable is kept as `<exe>.old` and
   removed on the next start. Without `-pre` the target is the newest GitHub release not marked as a pre-release;
   with `-pre` it is the newest release of any kind, so a full release wins over older pre-releases; `-v <version>`
   installs exactly that release, older or not. The TUI asks users on a pre-release whether to include
   pre-releases and asks before a downgrade (default no); the CLI never asks and stops a downgrade with exit 3
   unless `--accept downgrade` is given.
