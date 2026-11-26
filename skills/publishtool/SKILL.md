---
name: publishtool
description: Publish .NET projects into incremental deploy packages and deploy or roll them back on Windows servers (IIS apps and Windows services) with PublishToolCli, the non-interactive CLI of PublishTool. Use this skill whenever the user wants to install or update PublishTool, publish a repository or a branch with it, make a deploy package, deploy or roll back a package on a server, check or change PublishTool settings or server targets, or understand a PublishToolCli exit code, warning code or JSON result - also when they only say "publish uat", "make a package for prod" or "deploy this package", or mention PublishTool, PublishToolCli, publishtool-manifest.json or a package directory named after a branch and a timestamp. Not for working on PublishTool's own source code.
---

# PublishTool

PublishTool builds and publishes .NET projects with MSBuild and turns each publish into an incremental deploy package: only the files that changed since the previous publish of the same git branch, plus a manifest. On the server, the same tool applies a package to IIS apps and Windows services with a backup and an automatic rollback.

It ships as two executables. Use only `PublishToolCli`. The other one, `PublishTool`, is an interactive TUI that needs a real terminal and a person answering prompts; started from an agent it hangs or fails.

## Ground rules

- **Take the syntax from the tool, not from memory.** Run `PublishToolCli --help` and `PublishToolCli <command> --help` before the first use in a session. The help is short, lists every option, exit code and warning code, and always matches the installed version. This skill describes the workflow and the judgment around it.
- **Always pass `--json`** and read the result object (see below). The text output is for people.
- **Run one PublishToolCli command at a time.** Two commands writing the settings files at once can lose a change.
- **Know which commands change something.** A request that names the action is the go-ahead for it ("check that it still publishes" covers `publish --local`, "publish uat" covers `publish`). Anything the user did not ask for needs asking first, and `deploy --yes` / `rollback --yes` always need a go-ahead given after the user saw the dry run:

| Command | Changes |
|---|---|
| `version`, `update --fetch`, `config` without setting options | nothing |
| `deploy` and `rollback` without `--yes` | nothing (dry run) |
| `publish --local` | clears and refills `<publish dir>\<Project>` on this machine |
| `publish` | the same, plus a new package in the deploy directory, a new reference zip on the shared directory, and old reference zips deleted there |
| `config` with setting options | the stored settings (`config.json`, or `server.json` with `--prefix`) |
| `deploy --yes`, `rollback --yes` | the server: stops IIS and services, replaces files, starts them again |
| `deploy --yes --clean` | the same, and deletes every file on the targets that is not part of the publish |
| `update` | replaces the PublishToolCli executable |

- **The tool's error messages suggest follow-up commands** (`--accept <codes>`, `--target`, `--skip`, `rollback --yes`). Treat them as options to put to the user, never as the next command to run. They exist so a person can decide.

## Installing and updating the tool

Check first: `PublishToolCli version --json`. If the command is not found, tell the user and offer to install it. Putting an executable on their machine or server is their decision, so download and install only after they agreed, and ask where it should go (a directory that is on `PATH`).

- Releases: https://github.com/dalibor-osu/PublishTool/releases
- The asset is `PublishToolCli-<os>-<arch>-v<version>.zip` for Windows (`win-x64`, `win-arm64`) and `.tar.gz` for Linux (`linux-x64`, `linux-arm64`). Assets named `PublishTool-v<version>-...` are the interactive TUI, not what you need.
- Take the newest release that has a `PublishToolCli` asset. If the release marked as latest has none, the CLI exists only in pre-releases so far; say so and let the user pick the version.
- Each release has a `SHA256SUMS.txt`. Compare the downloaded archive's SHA-256 with its line there before unpacking.
- The archive contains one directory named like the asset, with `PublishToolCli.exe` (`PublishToolCli` on Linux) and a LICENSE. Copy the executable into the directory the user chose. It is self-contained, no .NET runtime is needed.
- Confirm with `PublishToolCli version --json`.

With the GitHub CLI the download is one command (leave the tag out for the latest full release):

```powershell
gh release download <tag> --repo dalibor-osu/PublishTool --pattern "PublishToolCli-win-x64-*.zip" --pattern SHA256SUMS.txt --dir <download dir>
```

What else a machine needs:

- To publish: `MSBuild` on `PATH` (Visual Studio or the Build Tools) and git, plus the settings of the repository (see Settings).
- To deploy and roll back: Windows, and an elevated terminal for `--yes`. No MSBuild or git.

Updating: `PublishToolCli update --fetch --json` reports the version that would be installed without changing anything, `PublishToolCli update` installs it (`-pre` includes pre-releases, `-v <version>` picks one). It replaces only the PublishToolCli executable. Keep the machine that publishes and the server on the same version, otherwise every deploy warns with `tool-version`.

## Running a command and reading the result

With `--json`, stdout carries exactly one JSON object and all progress goes to stderr. Capture stdout alone; merging the streams (`2>&1`, or a background run that collects both) breaks the JSON. The dependable way is to redirect stdout to a file outside the repository and read that, printing the exit code in the same command, because shell variables do not survive into your next tool call:

```powershell
PublishToolCli publish -dir C:\repo --json > $env:TEMP\publishtool-result.json; "exit=$LASTEXITCODE"
```

```powershell
Get-Content $env:TEMP\publishtool-result.json -Raw | ConvertFrom-Json
```

Every result has `command`, `status`, `exitCode`, `errors`, `warnings` (each with `code`, `message`, `accepted`, sometimes `files`) and `steps` (each with `name`, `state`, `detail`), plus fields of its command. The object is printed on failures too, so read `errors` instead of guessing from the exit code alone.

| Exit | Status | Meaning | What to do |
|---|---|---|---|
| 0 | `succeeded` | Done. A dry run is 0 whatever warnings it lists | Read `warnings` anyway |
| 1 | `failed` | The operation failed | Report `errors` and the failed step. A deploy refused up front changed nothing; one that failed after it started (it has `steps`) restored the server by itself. A `rollback --yes` that failed may have left the server half restored: stop and report, do not retry in a loop |
| 2 | `invalid-arguments` | Bad option or missing configuration | Fix the option, or ask the user for the missing setting |
| 3 | `warnings-not-accepted` | Stopped before changing anything | Show the warnings, let the user decide |
| 4 | `restore-failed` | A deploy failed and its automatic restore failed too | Stop. The server is in a mixed state; report it and let the user decide about `rollback` |
| 130 | `cancelled` | Ctrl+C | Report it; a cancelled deploy rolled itself back |

When something was logged, stderr names a log file with the full details.

## Publishing

A publish takes the working tree as it is and names the package after the current git branch: the branch is the package prefix (characters that cannot be in a file name, including `/`, become `-`), e.g. `uat_2026-01-31_14-05-00`, or `feature-cart-fix_...` for `feature/cart-fix`. The prefix decides which environment the package is for and which earlier publish it is compared with, and no option overrides it. A prefix with characters other than letters, digits, `.`, `_`, `-` and `+` fails the build step; report that instead of renaming the branch. "Publish uat" therefore means: publish while the branch `uat` is checked out. So before publishing:

1. Check the branch (`git branch --show-current`) and `git status`. If the branch is not the one the user meant, or there are uncommitted changes they did not mention (they would go into the package), stop and ask whether to publish as it is or to change the branch first. Switch branches, stash or pull only when the user says so.
2. Check the settings: `PublishToolCli config -dir <repo> --json`. If `missingSettings` is not empty, ask the user for those values and store them with `config`, or pass them for one run as options of `publish`. Do not invent paths.
3. Run the publish. It can take many minutes (MSBuild build, zip, upload to a network share), so give it a long timeout or run it in the background, and do not kill and restart it.

Use `--local` whenever the goal is only to see whether the projects build and publish. It needs only the publish directory and the projects (stored, or named with `--projects`), and the result to report is `projects` and the `steps` with their states. There is no need to stop and ask about the branch or uncommitted changes for it, but say what was built: the working tree as it is, with the branch as prefix, which the repository may use to pick its per-environment configs. A publish without `--local` is not a harmless check: it uploads a new reference zip to the shared directory, every later package of that branch is compared with it, and only the newest three references of a branch are kept. A throwaway publish therefore shifts the baseline, and the next real package would silently lack the changes that went into the throwaway one.

Without `--projects`, the projects stored as publishable are published (that is what "everything" means here); with `--projects`, exactly the named ones.

After a successful publish, report from the result: `packageName`, `packageDirectory`, and per entry of `changes` the `name`, `changed` and `deleted` counts, `base` (the package it builds on) and `isFullPublish`. A project that is missing from `changes` had no changes. `isFullPublish: true` means no earlier publish of this branch contained the project, which is expected the first time and worth mentioning otherwise. `carried` lists projects that were not published this time and were taken over from the previous reference.

The package directory then has to get to the server. That is the user's step unless they ask you to copy it.

## Deploying on a server

`deploy` runs on the server (Windows only), against a package directory. Always in three steps: dry run, show, deploy.

**1. Dry run.** `PublishToolCli deploy -dir <package dir> --json` verifies the package against its manifest and prints the plan. It changes nothing and needs no administrator rights.

- Exit 2 with `missingTargets`: the server does not know where those projects live. Ask the user for each project's target directory and, for a Windows service, its service name, or whether the project is not deployed on this server at all (skip). Then store it with `config --prefix <prefix> ...` or pass it for this run. Never guess a target path: the deploy overwrites and deletes files there.
- Exit 1: the package does not match its manifest (usually copied incompletely) or the plan has errors. Report `errors`.

**2. Show the plan and ask.** Give the user, per entry of `targets`: project, `path`, IIS or `serviceName`, the `create` / `overwrite` / `delete` counts, `deployedNow` and `basedOn`. Then every warning with its message (a message about one target starts with the project name), and its `files` when it has them. Mention `skipped` projects and `extraFiles` (files in the package that the manifest does not list; they are not deployed). Also say what the deploy does to the server: if any target is an IIS app, the whole IIS is stopped for the duration, so every site on that server is down, not only the deployed one. Check here whether the session is elevated and say so if it is not, because step 3 needs it and you cannot elevate yourself; the user has to start the session as administrator.

**3. Deploy.** Only after the user said to go ahead: `PublishToolCli deploy -dir <package dir> --yes --accept <codes> --json`, where `<codes>` are exactly the warning codes the user accepted, comma separated (`--accept config-changed,chain-mismatch`), and no `--accept` when there were none.

Accepting a warning is the user's decision about their server, made with the warning in front of them:

- An approval given before the warnings were shown ("accept whatever comes up") does not count. Show them and ask again.
- "Go ahead" that names only some of the warnings accepts only those. Ask about the others before running anything.

Afterwards report by exit code (table above). After a failed deploy do not simply run it again: read the failed step first.

Things to keep in mind:

- Packages of one prefix are deployed in the order they were published. A skipped package means its changes never reach the server. For `chain-mismatch`, `deployedNow` is where the target is and `basedOn` is the package this one expects there; the packages between them are the missing ones.
- The package directory holds the backup (`.backup`) after a deploy. Do not delete or tidy up package directories; without the backup there is no rollback.
- `deploy --yes` refuses a package whose `.backup` already exists (the dry run does not check this). Nothing was changed by the refusal. It usually means the package was deployed successfully before, so `rollback --yes` would undo a good deploy. Run `rollback` without `--yes` to read the state (next section), report it, and let the user choose.
- Do not add options that widen what a run does (`--target`, `--service`, `--skip`, `--clean`, `--keep` and similar) unless the user asked for them.

## Cleaning a target (`deploy --clean`)

A package only holds changes, so a normal deploy never touches files it does not mention. Over time targets collect files that no publish contains any more. `--clean` compares each target with the complete publish (`publishtool-inventory.json` in the package directory) and deletes what is not part of it. That is also everything that lives only on the server: logs, uploads, data files, certificates, things someone copied there by hand. Use it only when the user asked to clean, and treat the list of files as the thing to review, not the count.

If `deploy --help` does not list `--clean`, the installed version does not have it. A package without `publishtool-inventory.json` (made by an older version, or copied without it) fails with exit 1.

1. **Ask what must stay** before the first clean of a target: directories and files that exist only on the server. They become keep patterns, `<project>=<pattern>`: a pattern with `/` is relative to the target directory (`Web=logs/**`, `Web=wwwroot/uploads`), one without matches that name at any depth (`Web=*.log`, `Web=App_Data`), and a pattern matching a directory keeps everything below it. `config --prefix <prefix> --keep ...` stores them for every later clean (the user's call, like any setting); `deploy --keep ...` applies them to one run.
2. **Dry run** with `--clean`. On top of the normal plan the result has `clean: true`, per target `cleanFiles` and `cleanDirectories`, and:
   - `clean-delete` warnings: `files` is the complete list of what would be deleted; a path ending in `/` is an empty directory that would be removed.
   - `clean-mismatch` and `clean-missing` warnings: files that differ from the publish or are missing on the target. The clean does not repair them, it only reports; accepting them means deploying anyway.
   - `protectedConfigFiles`: config files on the target that are not part of the publish. They are never deleted, only listed.
   - `notCleaned`: projects of the publish without a target on this server.
   - Targets with `cleanOnly: true`: projects the package does not change but whose target would be cleaned. Their hosts are stopped too.
3. **Show the user what would be deleted**: every file, or for a long list the directories with counts plus the full list in a file, and point out anything that looks like data and not like a leftover of an old publish. If something must stay, add a keep pattern and run the dry run again. Do not accept `clean-delete` to "see what happens".
4. **Run it** with `--clean --yes --accept <codes>` only after the user accepted each clean warning that was shown. A clean warning that is not accepted stops the run with exit 3 before anything changes, also when the package itself has nothing to deploy (cleaning with an unchanged package is a normal use).

Deleted files and removed directories go into the package's backup, so `rollback` brings them back as long as the package directory is kept.

## Rolling back

`PublishToolCli rollback -dir <package dir> --json` without `--yes` is a dry run that needs no administrator rights: it shows `journalState`, and per target how many files it would restore and remove and which package the target would be at afterwards (`recordAfter`).

- `BackingUp`: the deploy stopped before it changed anything; `--yes` only removes the incomplete backup. After that the package can be deployed again, starting with a new dry run.
- `Deployed`: a completed deploy, the package is live. There is nothing to repair; rolling back undoes it, which is only right when the user wants the deploy gone.
- `Applying` or `RestoreFailed`: the deploy was interrupted or its restore failed; rolling back is how the server gets back to a consistent state.

Show this to the user and run `rollback --yes` only on their word. It needs an elevated terminal and stops the hosts like a deploy does. A package that was rolled back can be deployed again, with a new dry run first, because the server may have changed.

## Warning codes

A warning never blocks a dry run. With `--yes`, any warning whose code is not in `--accept` stops the run with exit 3 before anything changes. Explain each one that shows up; do not present any of them as routine.

| Code | Meaning | Worth checking |
|---|---|---|
| `tool-version` | The package was made by another PublishTool version than the one deploying it | Which versions; updating one side removes it |
| `target-missing` | The target directory does not exist and would be created | A typo in the path, or really a first deploy |
| `no-record` | The server has no record of a deploy to this target, but the package builds on an earlier one | Whether the earlier packages got there some other way |
| `unknown-base` | The package does not know what it builds on, so the order cannot be checked | What the target is at now |
| `chain-mismatch` | The target is at another package than the one this package builds on: one was skipped or deployed out of order | Which package is missing; normally deploy that one first |
| `already-deployed` | This package is already recorded as deployed to the target | Whether a re-deploy is intended |
| `full-publish` | The package holds a whole project while the target already has a deploy; files deleted since then are not removed | Why there was no earlier reference (new branch, renamed project) |
| `config-changed` | The deploy overwrites or deletes config files (`web.config`, `*.exe.config`, `*.dll.config`, `appsettings*.json`) | The listed `files`: server-specific settings would be replaced by what the repository ships |
| `target-modified` | Files on the server are not what the package expects to replace, so they were changed there or by a skipped package | The listed `files`: hand edits and hotfixes would be lost |
| `clean-delete` (`--clean`) | Files on the target are not part of the publish and would be deleted, empty directories removed | Every entry of `files`: anything that lives only on the server needs a keep pattern first |
| `clean-mismatch` (`--clean`) | Files on the target differ from the publish and the package does not contain them, so they stay as they are | Why they differ: a hand edit, or a package that never got there |
| `clean-missing` (`--clean`) | Files of the publish are missing on the target and the package does not contain them | Whether the app works without them; this deploy does not bring them |
| `newer-deploy` (rollback) | Another package was deployed to the target after this one | Rolling back restores only this package's files on top of the newer ones |
| `downgrade` (update) | The version to install is older than the running one | Whether that is intended |

The message names the first few files; the complete list is in the warning's `files`. A code that is not in this table means the tool is newer than this skill. Read what `--help` says about the option that produces it, show the message and the files to the user, tell them this skill does not cover it, and accept it only when they say so for that code.

## Settings

- Per repository directory (`config -dir <repo>`): the publish directory (local output), the shared directory (network share with the reference zips), the deploy directory (local, receives the packages), ignored directories and the publishable projects. Options given to `publish` override them for one run without storing anything.
- Per server and package prefix (`config --prefix <prefix>`): where each project is deployed, its Windows service if it is one, which projects are not deployed on that server, and the keep patterns `--clean` respects. Writing these usually needs an elevated terminal.

Showing settings is free; changing them is the user's call, because the next publish or deploy acts on them.
