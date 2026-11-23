# Server deploy command

Status: agreed
Date: 2026-11-23
Area: publish deploy step (DeployPreparer, PublishCommand), new deploy command, configuration (Config, ConfigHandler)

## Context of the change

Today PublishTool only covers the developer machine. `publish` without `-l/--local` builds and publishes the
selected projects, then `DeployPreparer` compares the output byte-wise with the newest reference zip of the
current branch on the share and writes the changed or new files to `<deploy dir>\<prefix>_<timestamp>\<Project>\...`
(see "The deploy workflow (DeployPreparer)" in `CLAUDE.md`, `PublishTool/DeployPreparer.cs`,
`PublishCommand.PrepareDeploy`). After that everything is manual. Someone copies the package to the server, stops
IIS (trw.ldp's `Scripts/rebuild_solution.ps1` uses `iisreset /stop` and `/start`), copies the files over the
deployed apps and starts IIS again.

What the current package cannot express or do:

- Deleted files. A file removed from a project is just absent from the package, which looks the same as
  "unchanged", so it stays on the server forever.
- Anything on the server: there is no backup, no rollback, and no way to know which package a server is on.
- Config is keyed by the working directory (`Configuration/Config.cs`, `DirectorySettings`). On the server the
  working directory is the package directory, whose name changes with every publish.

Facts about the target project (trw.ldp, see `CLAUDE.md.user`) that shape this design:

- 9 of the 11 publishable projects are IIS apps. The other two are Windows services: `Trw.Ldp.PrintJobProcessor`
  (`WinExe`, installed in `D:\apps\...`) and `EventLogRelay` (an `Exe` that calls `ServiceBase.Run` when it is not
  interactive, and writes `Logs\` into its own directory). `iisreset` unlocks neither of them.
- Server config files are maintained by hand. Some are encrypted in place with `aspnet_regiis`
  (`Scripts/Protect-LdpConfigs.ps1`), and the repo copies point at a local database.
- Release builds of PublishTool exist for linux and osx too, but IIS and Windows services are Windows only.

## User request

PublishTool's next big update should support usage on the target server where the apps it publishes get deployed.
The first part is a metadata file, probably JSON, with additional info. Its main purpose is to make removing
files that are no longer used possible: such files have to be marked for deletion somewhere, because a file that is
just missing from the deploy directory looks like a file that was not updated. The metadata file could hold
additional information in the future.

The tool is mainly built for IIS-hosted apps, and the first version should focus on that use case. Before files are
copied, IIS has to be stopped because it can lock .NET Core app files. The `iisreset` tool can do this.

Files are matched by their path, so config files for each environment have to be handled. The target project does
not do this today (config files are handled and updated manually). It has to become automatable, which means
changing logic in the target project itself.

A new `deploy` command handles IIS, copying the files, deleting all removed files, and starting IIS again (this may
require elevated permissions). It runs from a directory that contains the published project directories. For
example, after publishing "TestAppWeb" and "TestAppApi", it runs from a directory (usually named the way PublishTool
names deploy directories) with the subdirectories "TestAppWeb" and "TestAppApi", which hold all the files to
deploy. Each app can be deployed to a different path on the server, so each project (directory) name maps to a path
on the file system, stored the same way as the path config for publish.

The workflow: locally, publish whatever is needed with the current PublishTool workflow. On the server, copy the
changed published files to the server and run PublishTool with the `deploy` command. The deploy command:

1. Creates a backup of the currently deployed files (important). Only changed files are backed up, there is no need
   for a full copy of the app.
2. Stops IIS processes to unlock .NET Core files.
3. Copies files from the subdirectories to the destination paths mapped in the PublishTool config.
4. Starts IIS again.

Every step reports any error to the user. When an error occurs in any step after the backup step, the whole deploy
is rolled back: the original files go back to the destination directories and IIS is started again.

The user asked to brainstorm how to approach these changes.

Added later in the session: UAT and prod do not share a server, so stopping all of IIS is not a big deal. The
metadata file should also contain the PublishTool version that created the publish, and deploy should check it
against its own version. If they do not match, the user sees a warning and can continue after accepting the
risk.

## Decisions

### Which parts of docs/deploy.md does this spec cover? (The doc has three parts: a metadata file written at publish time, the new server-side deploy command, and per-environment config files.)

| Option | What it means |
|---|---|
| Metadata + deploy (Recommended) | Publish writes the metadata (with deletions) and the new deploy command applies it; making trw.ldp produce env configs is a separate change in that repo. |
| All three in PublishTool | PublishTool also generates per-environment configs at publish time; bigger, and ties the tool to one project's config scheme. |
| Deploy command only | No metadata in v1, so no deletions; smallest, but drops the main reason the metadata file exists. |

**Answer: Metadata + deploy (Recommended)**

### How should deploy treat config files in a package (Web.config, appsettings*.json, *.exe.config)? Server copies are hand-maintained and partly encrypted in place with aspnet_regiis.

| Option | What it means |
|---|---|
| Protect on server (Recommended) | Per-target list of protected files that deploy never overwrites or deletes; it reports which ones changed so you merge them by hand. |
| Exclude at publish | Publish leaves config files out of the package entirely; simple, but nobody is told when a config changed. |
| Overwrite like any file | Hard prerequisite: trw.ldp must first produce correct per-environment configs, otherwise an encrypted prod config gets replaced. |

**Answer: Overwrite like any file**

### How should deploy decide which files to delete on the server?

| Option | What it means |
|---|---|
| Explicit list (Recommended) | Publish writes files that were in the previous reference but are gone now into the metadata; deploy deletes only those. Server-only files are never touched. |
| Mirror the target | Deploy deletes every file in the target dir that is not in the full publish file list; catches drift, but also deletes logs, uploads and App_Data. |
| Mirror with exclusions | Mirror, but with a per-target list of excluded paths; more robust against drift, but one forgotten exclusion deletes production data. |

**Answer: Explicit list (Recommended)**

### A package only holds the difference from the previous publish of the branch, not from what is deployed. What should deploy do when a package was diffed against a publish that is not the last one deployed to that target (a package was skipped or applied out of order)?

| Option | What it means |
|---|---|
| Check, ask on mismatch (Recommended) | Metadata names its base publish, deploy remembers the last package per target, shows the mismatch and asks whether to continue (default no). |
| Check, refuse | Same check, but a mismatch stops the deploy unless a force flag is passed; safest, more friction when a mismatch is harmless. |
| No check | Your responsibility to deploy packages in order; simplest, but a skipped package silently leaves stale files and missed deletions. |

**Answer: Check, ask on mismatch (Recommended)**

### How should deploy find the target path of each project directory on the server? The package dir name changes every run (e.g. uat_2026-11-23_10-00-00), so the current per-working-directory config key does not work. Does one server host more than one environment (e.g. UAT and prod)?

| Option | What it means |
|---|---|
| Keyed by branch prefix (Recommended) | Mappings grouped by package prefix (uat, master); the package name picks the set. Handles UAT and prod on one server and can't deploy a uat package to prod paths. |
| One map per machine | Single project name -> path map on the server; simplest, but breaks as soon as one server hosts two environments. |
| Named env chosen at deploy | Mappings grouped by an environment name given by an option or prompt; independent of branch names, but a wrong pick deploys to the wrong app. |

**Answer: Keyed by branch prefix (Recommended)**

### How should deploy stop IIS so files are unlocked?

| Option | What it means |
|---|---|
| iisreset (Recommended) | iisreset /stop and /start, as in deploy.md and rebuild_solution.ps1; no extra config, but every site on the server is down during the deploy. |
| Stop app pools | Stop only the app pools of the targets (appcmd); other sites stay up, but each target needs its app pool name in the mapping. |
| app_offline.htm | Drop app_offline.htm into each target; no IIS control, but it does not reliably unlock legacy ASP.NET Framework apps. |

**Answer: iisreset (Recommended)**

### Stopping IIS needs administrator rights. How should deploy handle elevation?

| Option | What it means |
|---|---|
| Check and fail (Recommended) | Deploy checks it runs as admin before doing anything and stops with 'run from an elevated terminal'; simple and predictable. |
| Self-elevate via UAC | Deploy relaunches itself elevated; one less step, but the UI moves to a new console window and errors there are easy to miss. |

**Answer: Check and fail (Recommended)**

### Two publishable trw.ldp projects are not IIS apps: Trw.Ldp.PrintJobProcessor (Windows service in D:\apps) and EventLogRelay (console exe). iisreset does not unlock their files. What should deploy do for them?

| Option | What it means |
|---|---|
| Optional service name (Recommended) | A target can name a Windows service; deploy stops it before copying and starts it after, and rolls back like IIS. |
| Not handled in v1 | Deploy only copies; a locked exe fails the copy and triggers rollback, so you stop the service by hand first. |
| Pre/post commands | Each target can run arbitrary commands before and after the copy; flexible, but turns the mapping into a small scripting system. |

**Answer: Optional service name (Recommended)**

Note, found after this round: EventLogRelay is a Windows service too (it calls `ServiceBase.Run` when it is not
interactive), not a plain console exe. The answer covers it the same way.

### Where should deploy keep the backup of the files it overwrites or deletes?

| Option | What it means |
|---|---|
| Configured dir (Recommended) | A backup directory set once per prefix in the server config, one subdir per deploy; survives deleting the package and is never under a site IIS serves. |
| Inside the package | `<package>\.backup`; zero config, but deleting the package after a deploy also deletes its only backup. |
| Next to each target | `<target>.backup\<package>`; no config, but backups end up scattered across disks and possibly under wwwroot. |

**Answer: Inside the package**

### How many deploy backups should be kept?

| Option | What it means |
|---|---|
| Last 3 per prefix (Recommended) | Same rule as the reference zips on the share; older backups are pruned after a successful deploy. |
| Keep all | Nothing is ever deleted; full history, but the disk fills up and cleanup is manual. |

**Answer: Keep all**

### Should v1 include a manual rollback command that restores the last deploy after it finished successfully (e.g. the app turns out broken)?

| Option | What it means |
|---|---|
| Yes, rollback command (Recommended) | Restores the newest backup of a prefix with the same IIS/service stop and start; reuses the automatic rollback code path. |
| No, restore by hand | Only automatic rollback on failure; the backup is plain files you copy back yourself, smaller v1. |

**Answer: Yes, rollback command (Recommended)**

### What should deploy show before it changes anything on the server?

| Option | What it means |
|---|---|
| Summary + confirm (Recommended) | Shows prefix, targets, files to copy/delete per target and the chain status, then asks; -y skips the prompt for scripted runs. |
| Summary + confirm + dry run | Same, plus a --dry-run flag that prints the plan and exits; handy for checking a package, one more option to maintain. |
| No prompt | Starts right away; fastest, but a wrong package or prefix is only noticed after IIS is down. |

**Answer: Summary + confirm + dry run**

### What should Ctrl+C do once IIS or the services are stopped and files are being copied or deleted?

| Option | What it means |
|---|---|
| Roll back and restart (Recommended) | Treated like a failure: restore the backup, start IIS and services, exit 1; further Ctrl+C is ignored until the server is back up. |
| Ignore until done | Once IIS is stopped, Ctrl+C is ignored and the deploy finishes and starts IIS; cancel only works during the backup step. |

**Answer: Roll back and restart (Recommended)**

### What should deploy do with a package that has no metadata file (made by an older PublishTool or put together by hand)?

| Option | What it means |
|---|---|
| Refuse (Recommended) | Deploy needs the metadata for deletions and the chain check; it tells you to republish with the new version. |
| Deploy with a warning | Copies files only, no deletions and no chain check, after a warning; works with old packages but skips the safety checks. |

**Answer: Refuse (Recommended)**

### Where should the server-side deploy settings live (prefix -> project -> path/service mappings and the last deployed package per target)? Today config.json is per user in %APPDATA%.

| Option | What it means |
|---|---|
| Machine-wide file (Recommended) | Separate file in %ProgramData%\PublishTool; every admin on the server sees the same mappings and the same last-deployed record, so the chain check stays true. |
| Section in config.json | New section in the per-user %APPDATA% config; one file like publish, but a second admin has no mappings and a different last-deployed record. |

**Answer: Machine-wide file (Recommended)**

### Packages are copied to the server by hand. Should deploy verify the package contents against the metadata before stopping IIS?

| Option | What it means |
|---|---|
| Size + SHA-256 (Recommended) | Metadata lists every file with size and hash; deploy refuses a package with a missing, truncated or changed file, catching a half-finished copy. |
| Presence only | Checks every listed file exists; catches missing files but not truncated or altered ones, and no hashing cost. |
| No verification | Trusts the package directory as it is; simplest, a broken copy is only found when the app fails after deploy. |

**Answer: Size + SHA-256 (Recommended)**

### Backups live inside each package directory. Which deploy does the rollback command restore?

| Option | What it means |
|---|---|
| Package it runs from (Recommended) | Run from (or -dir) a package dir, restores that package's backup; asks (default no) if a newer package was deployed to one of its targets since. |
| Newest of a prefix | Server settings remember where each deployed package lives; rollback finds the newest one itself, but fails if that dir was moved or deleted. |

**Answer: Package it runs from (Recommended)**

### After a successful rollback, the server matches the package's backup again. What happens to that backup?

| Option | What it means |
|---|---|
| Delete it (Recommended) | It duplicates what is on the server now; the package can be deployed again cleanly. An exception to 'keep all'. |
| Keep it, marked rolled back | Nothing is ever deleted; deploying the same package again moves the old backup aside first (e.g. .backup-1). |

**Answer: Delete it (Recommended)**

### The chain check needs to know per project which package last published it, so partial publishes don't look out of order. Is it OK to add a small lineage file to the root of the reference zips on the share?

| Option | What it means |
|---|---|
| Lineage file in zip (Recommended) | Per-project chain check; older PublishTool versions ignore the file but drop it when they write a zip, and other tools reading the zips see one extra root file. |
| Per-package base only | Share format unchanged; the manifest names the previous reference as base, so after any partial publish the next deploy asks about projects it never touched. |

**Answer: Lineage file in zip (Recommended)**

### With -y, what should deploy do when it hits a warning that normally needs your acceptance (PublishTool version mismatch, or a package deployed out of order)?

| Option | What it means |
|---|---|
| Stop on warnings (Recommended) | -y only skips the final confirmation; any warning makes the deploy stop with exit 1, so a scripted run never goes past a warning nobody read. |
| Accept all warnings | -y means yes to everything; fully unattended, but warnings only end up on screen and in the log. |
| Separate accept flag | -y skips the confirmation, and a second flag (e.g. --accept-warnings) is needed to go past warnings; explicit, one more option to maintain. |

**Answer: Stop on warnings (Recommended)**

## High-level plan

**Prerequisite outside this repository.** trw.ldp has to produce correct per-environment config files in its
publish output (and re-apply `aspnet_regiis` encryption where it is used) before `deploy` touches a real server,
because deploy overwrites config files like any other file. That is a separate change in trw.ldp. PublishTool work
can start without it, but deploy must not be used on UAT or prod until it lands.

**1. Package metadata (publish side).** The deploy step of `publish` writes a manifest into every package. For each
published project it lists the files in the package with size and SHA-256, the files to delete, and the base the
project was diffed against. The manifest also records the PublishTool version that wrote it. The reference zip on the share gets a small lineage record so the next publish knows,
per project, which package last published it. Everything else (naming, the share layout, the five deploy rows)
stays as it is. Depends on nothing.

**2. Server settings.** A machine-wide settings file in `%ProgramData%\PublishTool`, versioned through the
generic config store with a parser and migrator registry of its own. It holds the mappings per package prefix (project name -> target
path, optional Windows service name) and the record of the last package deployed to each target path. A wizard in
the style of `ConfigHandler` asks for anything that is missing. Depends on nothing; can run in parallel with 1.

**3. `deploy` command.** Runs from a package directory (`-dir` like the other commands), with `-y` to skip the
confirmation and `--dry-run` to stop after the summary. Phases: preflight (nothing changes: platform and admin
check, manifest verification, tool version check, mappings, chain check, summary, confirmation), backup (writes only into the
package), stop, apply (copy and delete), start, record. Any failure or Ctrl+C after the backup phase restores the
backup and starts IIS and the services again. Depends on 1 and 2.

**4. `rollback` command.** Runs from a deployed package directory and restores its backup with the same stop and
start handling. It resets the last-deployed record and deletes the backup afterwards. If a newer package was
deployed to one of its targets since, it asks first (default no). It reuses the restore path from 3, so it comes
after 3.

**5. Documentation.** The repository instructions (layout, deploy workflow, usage guide) and the help texts of the
new options, updated with each phase rather than at the end.

## Architecture decisions

**The manifest is the contract between publish and deploy.** Deploy is driven by the manifest only. It never
treats "a directory exists in the package" as a project, or "a file exists" as something to copy. A package
without a manifest is refused. Extra files in a project directory that the manifest does not list are shown in
the summary and never deployed. The manifest is a versioned JSON model registered in `JsonContext` (Native AOT,
no reflection), so later fields can be added the way config versions are. The package name and prefix come from the
manifest, not from the directory name, because deploy.md says the directory is only "usually" named that way.
The manifest is the last thing the publish deploy step writes, after the new reference zip is committed on the
share. A package whose compress or upload failed is still left in the deploy directory today, but it has no
reference behind it. Without a manifest, deploy refuses it, so it cannot break the chain. Reason: a package is
copied by hand and can be incomplete or edited. A manifest with hashes makes "what deploy will do" checkable
before IIS stops.

**Two versions in the manifest, checked differently.** The manifest format version says whether deploy can read
the file at all. A format newer than the deploy tool understands is refused, like a newer `config.json`. The
PublishTool version (`BuildInfo.Version`, e.g. `1.4.0`, `1.5.0-rc.1` or `dev`) records which tool wrote the
package. Deploy compares it with its own version as an exact string match. A mismatch is a warning shown in the
summary that the user has to accept before deploy continues. It never blocks on its own, because a format
mismatch is already caught by the format version. `rollback` does not compare tool versions; the backup journal
carries its own format version instead.

**Deletions are an explicit list computed at publish time.** For each published project: files present in the
previous reference but absent from the new publish. Paths are compared case-insensitively. A file renamed only in
case is a changed file and is never also a deletion, otherwise deploy would copy it and then delete it. Projects
carried over (not published this time) get no deletions. Deploy never deletes anything that is not on the list,
so logs (EventLogRelay writes `Logs\` next to itself), uploads and server-only files are safe.

**The chain check is per project, not per package.** Packages from partial publishes are common. With a
per-package base, publishing only Web and then only Api would make the Api package look out of order. The
reference zip therefore carries, per project, the name of the package that last published it (carried-over
projects keep their value). The new manifest's base for a project is that value. On the server, the last-deployed
record is keyed by the absolute target path, not by prefix, so any prefix mapped to the same directory sees the
same state. A mismatch, an unknown base (first publish after the upgrade, or a reference without lineage) or a
target with no record is shown in the summary and asked about, default no.

**Server settings are separate from `config.json`.** Publish settings are per user and per repository. Deploy
settings are per machine, keyed by package prefix, and written by an elevated process, so a second admin sees the
same mappings and chain state. Adding a new prefix or a new project to a prefix is a wizard question, not a new
format version.

**Rollback invariant.** The backup is complete on disk before anything on the server changes. It is stored in
`<package>\.backup` and consists of the original copies of every file the apply phase overwrites or deletes, plus
a journal. The journal lists the files the apply phase creates, the previous last-deployed record per target, and
its own state (backed up, applying, deployed, rolled back). Restore copies every backed-up file back and deletes
every created file. It is idempotent, so it does not need to know how far apply got, and it works the same for
a failed, cancelled or killed deploy. `rollback` accepts a journal in state "applying" (process killed, window
closed) and skips the chain check in that case.

**One backup per package.** If `.backup` already exists, deploy refuses and points at `rollback`. Running the
same package twice would otherwise overwrite the real backup with copies of the package's own files. After a
successful rollback the backup is deleted, so the package can be deployed again. That is the only exception to
"keep all": backups are otherwise never pruned.

**Stopping and starting.** A target with a service name is stopped and started as that Windows service. A target
without one is an IIS app, and `iisreset` runs once if any IIS target is in the package. Stop order is services,
then IIS; start order is the reverse. A failure to start after a successful copy also triggers a rollback, as
deploy.md asks for every error after the backup step.

**Cancellation during deploy.** `Program.cs` today ignores the second Ctrl+C without setting `e.Cancel`, so the
process is killed, and it returns 0 for a cancelled command. Between the start of the stop phase and the end of
restore, every Ctrl+C has to be swallowed, and a cancelled deploy exits 1. Before the stop phase, cancel just
stops and removes the partial backup.

**Platforms and runs.** `deploy` and `rollback` exist in every build because the command parser is generated
per project, not per RID. On non-Windows systems they exit 1 with a "Windows only" message before reading
anything. They run in the alternate screen with `JobDisplay` rows like `publish`. With `-y` the command does not
wait for a key at the end, because it is meant for scripted runs.

**Warnings versus `-y`.** Everything that normally needs the user's acceptance (tool version mismatch, chain
mismatch, unknown base, a target with no record, a newer package deployed before a `rollback`) is a warning. With
`-y`, the only thing skipped is the final confirmation. Any warning stops the run with exit 1 before anything
changes, so the first deploy of each target after the upgrade has to be run interactively once.

## Weaknesses and risks

| Risk | Likelihood / cost | What reduces it |
|---|---|---|
| A package with a changed `Web.config` / `appsettings.json` overwrites the hand-maintained, possibly encrypted server copy with the repo version, which points at a local database | High until trw.ldp produces per-environment configs; outage of the environment | Do not deploy to a real server before the trw.ldp change; open: let the summary highlight config files in the package |
| `iisreset` stops every site on the server, including sites unrelated to the package | Certain on every deploy; low cost because UAT and prod are on separate servers | Short stop window (backup and verification happen before the stop); app pool stop as a later option |
| Version check gives false comfort or false alarms: two different `dev` builds both report `dev`, and a patch release that changed nothing in the package format still warns | Medium; one extra question, or a missed difference between dev builds | Exact match keeps the rule simple; the format version catches real incompatibilities |
| Deleting a package directory deletes its backup, and with "keep all" packages pile up on the server | Medium; no rollback for that deploy, disk use | Document it; the summary says where the backup lives |
| Process killed or window closed while IIS is stopped (Ctrl+C is handled, closing the console is not) | Low; sites stay down, files half-copied | Backup and journal are complete before the stop; `rollback` from the package restores and starts IIS |
| Restore itself fails (disk full, file still locked) | Low; a broken site that needs manual work | Restore keeps going after a per-file error, reports every failed file, leaves the backup in place and prints its path; exit 1 |
| Reading in-use files for the backup while IIS still runs | Low; loaded assemblies are opened for shared read | A read error fails the backup phase, before anything on the server has changed |
| Chain record drifts after a manual deploy outside the tool | Medium; false "in order" or false mismatch | Mismatch only asks, never blocks; the summary shows both package names |
| The first deploy of every target after the upgrade has no lineage or record, so it always asks | Certain, once per target | Expected; the summary explains why |
| An older PublishTool publishes the same branch in between and writes a reference zip without lineage; other tools that read the zips (the `ldp-deploy-check` skill) see an extra root file | Medium while versions are mixed; one extra "unknown base" question | Update every developer machine; check the skill ignores root files |
| User answers "continue" on a real mismatch, so files deleted in a skipped package stay on the server | Low; stale files, harmless in most cases | Summary names the skipped base; deploying packages in order |
| A wip branch package has a new prefix and its wizard maps it to prod paths by mistake | Low; deploy to the wrong environment | Summary shows prefix and every target path before confirmation |
| A package left behind by a failed compress or upload (it has no reference on the share) gets deployed, so every later package is diffed against an older base | Low; stale files on the server | The manifest is written only after the upload commits, so deploy refuses such a package |
| Two admins deploy on the same server at the same time | Low; interleaved files, wrong chain record | Open: a machine-wide lock file in `%ProgramData%\PublishTool` |
| A service that takes long to stop, or has dependent services | Low to medium; copy fails on a locked file | Wait for the stopped state with a timeout and fail into rollback |

Open questions:

- Exact timeouts for service stop and start, and whether the summary should flag config files. Both are decided
  during implementation.

## Out of scope

- Generating or transforming per-environment config files (a separate change in trw.ldp, and a prerequisite).
- Narrower IIS control (stopping single app pools, `app_offline.htm`) and self-elevation through UAC.
- Mirroring the target or detecting drift between the server and the package base.
- Pre/post commands per target, beyond stopping and starting one Windows service.
- Automatic pruning of backups or packages on the server.
- Deploying packages without a manifest (made by older PublishTool versions).
- Moving the package to the server (it stays a manual copy) and zipping packages.
- Running deploy remotely from the developer machine, and deploy on Linux or macOS.
- Database changes (SQL scripts) that go with a release.
