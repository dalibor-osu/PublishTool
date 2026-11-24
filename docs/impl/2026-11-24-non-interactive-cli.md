# Non-interactive CLI

Spec: docs/specs/2026-11-24-non-interactive-cli.md
Date: 2026-11-24
Status: implemented
Area: PublishTool.Core (new), PublishTool (TUI), PublishToolCli (new), PublishTool.Generators, PublishTool.Tests (new), release workflow, docs

## What was done

- `PublishTool.Core`: everything that is not presentation moved out of the TUI (config, deployment, GitHub, helpers,
  `BuildInfo`), plus new pieces: the progress seam (`Progress/IStep.cs`), `PublishWorkflow` (the former publish logic of
  `PublishCommand`, returning `PublishResult`), coded deploy and rollback warnings (`DeployWarning`, `WarningCodes`),
  `DeployRunner`/`RollbackRunner` on `IStepList` with `DeployOutcome.RestoreFailed`, non-mutating override resolution
  (`DeployTargets`, `DirectorySettings.With`/`MissingFor`), and `SelfUpdate` (per-executable asset name, exact version).
- `PublishTool` (TUI) is a thin front end over the core with the same prompts and progress table; `publish -a`,
  `deploy -y` and `rollback -y` are gone and point to the CLI; `update -v` now installs that exact version.
- `PublishToolCli`: commands `publish`, `config`, `deploy`, `rollback`, `update`, `version`; text output with one line
  per step, `--json` (one object on stdout, progress on stderr, `schemaVersion` 1), exit codes 0/1/2/3/4/130,
  `--yes` as the go-ahead (dry run without it), `--accept <codes>`, per-run `--target`/`--service` and directory overrides.
- The generator takes the app name, help command and generated namespace from each project's `RootNamespace`.
- Release workflow builds and packages both exes; CLI assets are `PublishToolCli-<rid>-v<version>`, not the
  `PublishToolCli-v<v>-<rid>` of the spec's release decision: older TUIs would match that name and install the CLI over
  themselves, so the order was put to the user during implementation and recorded as a new decision in the spec.
- `PublishTool.Tests` (xUnit, 71 tests) covers the core, the CLI option parsing and the CLI exit code mapping.
- Checks: `dotnet build PublishTool.slnx -c Debug --no-incremental` 0 warnings; `dotnet test PublishTool.Tests` 71 passed;
  AOT publish of both exes for win-x64 without warnings, both print version `9.9.9-rc.1` for `-p:BuildVersion=9.9.9-rc.1`;
  the CLI scenarios below were run against the Debug and the AOT build (not elevated).

## How to test

**Automated coverage.** `dotnet test PublishTool.Tests` (or the IDE test runner) for the core and CLI parsing. There is no
end-to-end suite in the repository; the steps below are the manual end-to-end path. The elevated server path (real IIS
and services) is in `TestApps/README.md`, steps 1-8 (step 8 is the CLI variant) and was not run in this session.

**Setup.** Build with `dotnet build PublishTool.slnx -c Debug`; MSBuild must be on `PATH`. In PowerShell:

```powershell
$cli = "$PWD\PublishToolCli\bin\Debug\net11.0\PublishToolCli-dev.exe"
$t = "$env:TEMP\PublishToolCliTest"
New-Item -ItemType Directory "$t\repo", "$t\publish", "$t\share", "$t\deploy" | Out-Null
Copy-Item -Recurse TestApps\CoreWebApp, TestApps\CoreServiceApp, TestApps\Directory.Build.props "$t\repo"
Set-Content "$t\repo\.gitignore" "bin/`nobj/"
git -C "$t\repo" init -b uat; git -C "$t\repo" add -A; git -C "$t\repo" commit -m v1
$dirs = '--publish-dir', "$t\publish", '--shared-dir', "$t\share", '--deploy-dir', "$t\deploy"
```

**Steps.**

1. `& $cli` - prints the help to stderr, exit code 2 (`$LASTEXITCODE`).
2. `& $cli --help` - help plus the exit code table and the warning codes, exit 0.
3. `& $cli publish -dir "$t\repo" --local` - exit 2, `error: Missing settings for ...: publish-dir, publishable ...`.
4. `& $cli publish -dir "$t\repo" --projects Nope @dirs` - exit 2, `No project named Nope was found`.
5. `& $cli publish -dir "$t\repo" --projects CoreWebApp,CoreServiceApp @dirs --json > v1.json` - exit 0; stderr shows
   `[start]`/`[done]` lines for build, two publishes and five deploy steps; `v1.json` parses as JSON with
   `status: succeeded`, `packageName: uat_<timestamp>`, `packageDirectory` existing under `$t\deploy`, two `changes` with
   `isFullPublish: true`, and `$t\share\uat_<timestamp>.zip` exists.
6. `$pkg = (Get-Content v1.json | ConvertFrom-Json).packageDirectory; & $cli deploy -dir $pkg` - exit 2,
   `No target directory is set for CoreServiceApp, CoreWebApp ...`, nothing written to `C:\ProgramData`.
7. `& $cli deploy -dir $pkg --target "CoreWebApp=$t\targets\web" --target "CoreServiceApp=$t\targets\svc" --service CoreWebApp=Spooler --service CoreServiceApp=W32Time`
   - dry run: plan lines per project, `warning [target-missing]` twice, `Dry run, nothing was changed. Run again with --yes
   to deploy.`, exit 0, `$t\targets` does not exist.
8. The same with `--yes` in a non-elevated terminal - exit 1, `deploy --yes stops IIS and services, run it from an
   elevated terminal`.
9. The same as step 7 with `--service CoreWebApp=NoSuchService123` - exit 1, `The Windows service NoSuchService123 does not exist`.
10. Change `CoreWebApp\version.txt`, delete `CoreWebApp\wwwroot\obsolete.txt`, commit, then
    `& $cli publish -dir "$t\repo" -p CoreWebApp @dirs --json` - exit 0; `changes[0].base` is the step 5 package,
    `isFullPublish: false`, `deleted: 3` (the file and its `.br`/`.gz` copies), `carried: ["CoreServiceApp"]`.

**Also worth trying.**

- `--json` on any failing command still prints exactly one JSON object on stdout (exit code repeated in `exitCode`),
  also for a command line that cannot be parsed (`& $cli nope --json`, `& $cli publish -c Fast --json`: exit 2).
- `& $cli update --fetch -v 0.2.0` reports `target: v0.2.0`, exit 0; `-v 99.0.0` is exit 1 (`There is no release v99.0.0`);
  the TUI's `PublishTool-dev.exe update --fetch -v 99.0.0` reports the same, exit 1.
- `& $cli config -dir "$t\repo" --publish-dir "$t\publish" --publishable corewebapp` stores `CoreWebApp` (canonical name)
  in `%APPDATA%\PublishTool\config.json`; then `& $cli publish -dir "$t\repo" --local` publishes only CoreWebApp;
  `--publishable Nope` is exit 2 and saves nothing; `--unset publishable,publish-dir` clears them. Back the file up first.
- `& $cli config --prefix uat --target "CoreWebApp=$t\t\web"` writes `C:\ProgramData\PublishTool\server.json` (needs an
  elevated terminal if the file exists); `--service Other=W32Time` for a project without a target is exit 2;
  `--remove-target CoreWebApp` removes it; combining `--prefix` with `--publish-dir` is exit 2.
- `& $cli deploy --accept bogus` and `& $cli publish -c Fast` are exit 2 with the valid values in the message.
- `& $cli rollback -dir $pkg` on a package never deployed is exit 1 (`journal.json was not found`).
- `& $cli update --fetch --json` reports `targetVersion`; on a build newer than the latest release it lists a
  `downgrade` warning, and `update` without `--accept downgrade` stops with exit 3.
- TUI: `PublishTool-dev.exe -a` and `PublishTool-dev.exe deploy -y` print the pointer to PublishToolCli, exit 1;
  `PublishTool-dev.exe --help` ends with the PublishToolCli hint.

## What is missing

Nothing - the spec is implemented in full. Its deferred MCP server mode is not part of it.

## Known problems

- The elevated paths (`deploy --yes`, `rollback --yes`, the TUI deploy/rollback) did not run against real IIS and
  services in this session; they are covered by the runner tests with a fake host controller only.
- The TUI's interactive prompts were not exercised (no terminal here); its publish path is the same `PublishWorkflow`
  the CLI ran.
- The release workflow was not run; the packaging steps were rewritten for two exes and are untested until a dry run.
- Concurrent writers of `config.json`/`server.json` (two tools or parallel agents) can still lose an update; the
  spec's mitigation (re-read before write, lock) is not built.
- Decided here because the spec left it open: missing administrator rights for `--yes` is exit 1; CLI option names are
  as in `CLAUDE.md`; `update -v` counts as consent to a downgrade in both exes; the CLI accepts `-y` as well as `--yes`.
- `SelfUpdate` no longer falls back to "the only file in the archive" when the expected exe is missing; an archive
  without the exact exe name now fails the update instead.
- `PublishTool.Tests` references the CLI exe project to test its parser, so the test build also compiles the CLI.

## Recommended follow-ups

- Run the Release workflow with `dry_run: true` to check that both archives per RID are built and named as expected.
- Run the elevated end-to-end test in `TestApps/README.md` including step 8 (CLI) before the first release.
- Run `dotnet test` in CI (a pull request workflow), now that the repository has tests.
- Add a read-modify-write lock around config saves before agents run in parallel on one machine.
- The MCP server mode the spec deferred, on top of the same core.

## Changelog

- Add PublishToolCli, a non-interactive command line tool for scripts and AI agents, available as its own download
- PublishToolCli prints a JSON result with --json and returns distinct exit codes for failures, bad arguments, unaccepted warnings, a failed restore and cancellation
- PublishToolCli deploy and rollback are dry runs unless --yes is given; warnings are accepted one by one by their code with --accept
- PublishToolCli config shows and changes directory settings and server deploy targets without prompts
- update --version installs an exact release
- Remove publish -a, deploy -y and rollback -y from PublishTool; unattended runs use PublishToolCli
- PublishTool and PublishToolCli each update only themselves
