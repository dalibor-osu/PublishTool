# Environment config support

Spec: docs/specs/2026-11-24-environment-config-support.md
Date: 2026-11-24
Status: implemented
Area: PublishTool.Core (PublishWorkflow, PublishCommandBuilder, DependencyBuild, MsBuildProperties, DeployPreparer,
Deployment/PackageManifest, DeployPlan, DeployWarning, ConfigFiles), PublishToolCli (results, deploy options), PublishTool (deploy)

## What was done

- `PublishWorkflow` resolves the prefix once, before the build, also with `--local`. It is passed as
  `/p:PublishToolPrefix=<prefix>` to every publish (`PublishCommandBuilder`) and into the traversal project's restore and
  build `Properties` (`DependencyBuild`). The build row shows the value. `PublishResult.Prefix` and the CLI publish
  JSON carry it (`prefix`, additive).
- `MsBuildProperties.PrefixProblem` accepts letters, digits, `.`, `_`, `-`, `+`. Any other character fails the build
  step before MSBuild runs, and every publish and deploy row is skipped. Escaping was not used.
- Manifest (format still 1, changed in place): `ManifestFile.PreviousSha256` holds the hash of the replaced reference
  file. `ManifestProject.Deleted` is now a list of `{ path, sha256 }` (`ManifestDeletion`).
- `DeployPlan.Create` (now takes a `CancellationToken`) adds two warnings per target: `config-changed` (fixed
  patterns in `ConfigFiles`, overwrites and deletions only) and `target-modified` (hash rule from the spec; unreadable
  files are listed as "(could not be read)"). `DeployWarning` gained `Files`. The message caps the list at 10, and
  the CLI JSON warning gains a full `files` array.
- Both codes are in `WarningCodes.All`, so `--accept` and `PublishToolCli --help` know them. CLAUDE.md is updated.
- Checks: `dotnet build PublishTool.slnx -c Debug --no-incremental` 0 warnings, 0 errors; `dotnet test PublishTool.Tests`
  111 passed; Native AOT publish of both executables (win-x64) without warnings. Steps 1-5 below and the `bad;name`
  case were run by hand and behaved as described; step 6 was not run (needs elevation). A real `publish --local` of
  `TestApps` (FrameworkWebApp, FrameworkServiceApp, CoreWebApp) succeeded with the property on the command line for
  all three project kinds; only the SDK kind was checked to receive the value (`prefix.txt`).

## How to test

**Automated coverage.** There is no end-to-end suite in the repository (CLAUDE.md lists MSBuild publishing and real
IIS as not covered). Unit tests: `MsBuildPropertiesTests`, `DeployPlanTests`, `DeployPreparerTests` and
`CliOutputTests`, run with `dotnet test PublishTool.Tests`. Steps 1-5 below were run by hand with the dev CLI; step 6
was not.

**Setup.** `dotnet build PublishTool.slnx -c Debug`. MSBuild must be on `PATH` (Visual Studio or Build Tools), and the
shell must be PowerShell. Create a throwaway git repository on branch `uat` with one SDK project `Web`
(`net10.0` library) that has a `web.config` with `CopyToPublishDirectory="Always"` and this target:
`<Target Name="WritePrefix" AfterTargets="Publish"><WriteLinesToFile File="$(PublishDir)prefix.txt"
Lines="PublishToolPrefix=$(PublishToolPrefix)" Overwrite="true" /></Target>`. Commit it. Use empty directories
`publish`, `shared`, `deploy`, `target` next to it. Use only per-run options, so the real config is never touched.

**Steps.**

1. `PublishToolCli-dev.exe publish -dir <repo> --projects Web --publish-dir <publish> --local --json`. Exit 0,
   `"prefix": "uat"`, and `<publish>\Web\prefix.txt` contains `PublishToolPrefix=uat`.
2. Run the same command without `--local`, adding `--shared-dir <shared> --deploy-dir <deploy>`. Exit 0, a package
   `uat_<timestamp>` is created.
3. Change `web.config` in the repository, commit, and repeat step 2. In the new package's
   `publishtool-manifest.json`, the `web.config` entry has both `Sha256` and `PreviousSha256`, and they differ.
4. Copy `<publish>\Web\*` to `<target>`, then overwrite `<target>\web.config` with other content.
5. `PublishToolCli-dev.exe deploy -dir <second package> --target Web=<target> --service Web=Spooler --json` (no
   elevation; a dry run never touches the service). Exit 0, `"status": "succeeded"`. `warnings` holds `no-record`,
   then `config-changed` with `"files": ["web.config"]`, then `target-modified` with `"files": ["web.config"]`.
6. In an elevated terminal, repeat step 5 with `--yes --accept no-record`. It stops with exit 3 and asks for
   `--accept config-changed,target-modified`. Nothing on the target changes.

**Also worth trying.**

- Leave `<target>\web.config` equal to the published file before step 5. Only `no-record` and `config-changed` warn.
- Check out a branch named `bad;name` and repeat step 1. Exit 1, the build step fails with the
  `PublishToolPrefix` message, and the publish step is skipped with "no usable package prefix".
- Lock a target file (open it with `FileShare.None`) during a dry run. It is listed under `target-modified` as
  "(could not be read)".
- Deploy a package whose target has more than 10 modified files. The message ends with "and N more", and `files`
  lists all of them.
- Publish outside a git repository. The prefix is `DEPLOY` and is passed like any other prefix.

## What is missing

- The spec asks for a test that the value MSBuild receives equals the manifest's `Prefix`. Only the parts are tested
  (both command lines and the traversal project). `PublishWorkflow` always runs MSBuild and has no seam to test it
  without MSBuild. The manual run above checked it once, for an SDK project.

## Known problems

- Branch names with characters outside the allow-list (`#`, `@`, `!`, `(`, `,`, `;`, ...) used to publish; now they
  fail at the build step. Escaping could allow some of them, but the spec chose "unchanged or fail".
- A manifest written before this change does not parse at all (`Deleted` changed shape), so deploy says it "could not
  be read" instead of something specific. Acceptable because no such package was deployed, but the message does not
  say "publish again".
- Planning reads and hashes every overwritten and deleted target file, and the backup reads them again. A large
  package's dry run takes longer than before.
- For legacy web and legacy library projects, only the command line is tested. No run checked that their build reads
  the value (the TestApps run only proves MSBuild accepts it).
- The TUI shows only the capped list. The full list needs the CLI with `--json`.
- MSBuild's incremental build can reuse intermediate files built for another prefix in the same checkout. This must
  be handled in the published repository (risk from the spec).

## Recommended follow-ups

- In trw.ldp, read `PublishToolPrefix` in `Directory.Build.props` (`uat` -> `UAT`, `prod` -> `Production`). Make its
  transform targets rerun when the prefix changes, so a `uat` then `prod` publish from one checkout cannot mix configs.
- Give deploy a specific error for a manifest in the earlier shape ("publish again with this version") before the
  first real package goes out.
- Consider escaping instead of refusing more characters, if a real branch name hits the allow-list.

## Changelog

- Pass the git branch prefix to MSBuild as `PublishToolPrefix`, so a project can pick its per-environment configs at
  publish time
- Refuse to publish from a branch whose name has characters that cannot be passed to MSBuild safely
- Warn before deploy overwrites or deletes a config file (`config-changed`)
- Warn before deploy overwrites or deletes a file that was changed on the server (`target-modified`)
- List every affected file of a deploy warning in the CLI's JSON output (`files`)
- Show the package prefix in the CLI's publish JSON output (`prefix`)
