# Environment config support

Status: agreed
Date: 2026-11-24
Area: publish (PublishWorkflow, PublishCommandBuilder, DependencyBuild), package manifest (DeployPreparer,
PackageManifest), server deploy (DeployPlan, warning codes, deploy summary of the TUI and the CLI)

## Context of the change

`docs/specs/2026-11-23-server-deploy-command.md` decided that `deploy` overwrites config files like any other file
and named the consequence as a prerequisite: the published repository has to produce correct per-environment config
files before `deploy` touches a real server. Its risk table lists "a package with a changed `Web.config` /
`appsettings.json` overwrites the hand-maintained, possibly encrypted server copy" as high, and leaves "let the
summary highlight config files in the package" as an open point.

The repository PublishTool is mainly used for (trw.ldp, see `CLAUDE.md.user`) is now going to produce those configs
at build time: XDT transforms for its .NET Framework apps and `appsettings.<Env>.json` plus the SDK's
`EnvironmentName` publish property for its ASP.NET Core apps. A session in that repository wrote a brief of what
PublishTool is missing for this, `PROPOSED-CHANGES-per-environment-configs.md` in the repository root (uncommitted,
deleted once this spec was agreed).
It plans four changes, numbered as in that brief:

- 1. Pass the package prefix to MSBuild, so the build knows which environment it publishes for.
- 2. Flag config files in the deploy summary.
- 3. Detect files that were edited by hand on the server before they are overwritten.
- 5. Record and show the environment of a package.

The brief leaves out a generic `--property key=value` passthrough (4) and post-deploy hooks (6).

What exists today:

- `PublishCommandBuilder.BuildCommand` passes `Configuration` and fixed publish properties per project kind;
  `DependencyBuild.WriteTraversalProject` builds the traversal project with `Properties="Configuration=..."`.
  Nothing tells the build which branch or environment is being published.
- The prefix (sanitized git branch, `DEPLOY` outside a repository) is resolved by `DeployPreparer.ResolvePrefixAsync`
  inside `PublishWorkflow.PrepareDeploy`, after the build and the publishes, and not at all with `--local`.
- `ManifestFile` carries `Path`, `Size` and `Sha256` of the new file. `ManifestProject.Deleted` is a list of paths.
  Nothing describes the file a package replaces. `PackageManifest.CurrentFormat` is 1; `PackageJson.Read` refuses a
  format newer than the tool and accepts every older one.
- `DeployPlan.FillFiles` sorts the files of a target into create and overwrite by `File.Exists` only. The warning
  codes (`WarningCodes`) cover the tool version, a missing target directory and the chain check.
- The TUI summary (`DeploySummary.WritePlan`) prints the package name and prefix in its first line; the CLI result
  carries the prefix too.
- trw.ldp has no consumer of a PublishTool property yet, so its name is still free. Its hand-collected server
  configs (`configs/uat`, `configs/prod`, untracked) consist of `web.config`, `appsettings.json`,
  `appsettings.Development.json`, `*.dll.config` and `*.exe.config` files only.

## User request

We would like to change how config files work in the ldp repository. Another agent prepared proposed changes that
would make this change easier, and I would like to implement them. Let's brainstorm the changes; they are described
in the uncommitted file in the repository root.

Added during the session: PublishTool should not be aware of the trw.ldp repository at all. It should still act as
a separate tool that can be used for any repository, even though we are focusing on that one. The manifest has not
been used yet, so keeping its format at 1 without a marker is fine.

## Decisions

### How should the build of the published repository (trw.ldp) learn which environment PublishTool is publishing for?

| Option | What it means |
|---|---|
| Pass prefix, repo maps (Recommended) | PublishTool adds one MSBuild property with the package prefix (uat, prod); trw.ldp's Directory.Build.props turns it into EnvironmentName. The mapping is versioned with the repo and the tool knows nothing about environments. |
| Tool maps prefix to env | New per-directory setting (uat=UAT, prod=Production) and PublishTool passes EnvironmentName itself. The tool knows the environment, but every machine must configure it and a missing mapping silently publishes development configs. |
| Property list per prefix | config.json stores arbitrary key=value MSBuild properties per prefix. Most flexible, largest surface to maintain, and the same per-machine risk as the tool-owned mapping. |

**Answer: Pass prefix.** In the user's words: "Publish tool should not be aware of the trw.ldp repo at all. It
should still act as a separate tool that can be used for any repo, even tho we are focusing on that one."

### Should 'publish --local' (publish only, no deploy package) pass the same property, so a local publish from the uat branch also produces UAT configs?

| Option | What it means |
|---|---|
| Yes, always (Recommended) | The publish output is identical with and without --local; the prefix is resolved once before the build in both modes. |
| No, packages only | --local keeps the development configs, handy for running the output on your machine, but the same branch then produces different files depending on the mode. |

**Answer: Yes, always (Recommended)**

### What should the MSBuild property that carries the package prefix be called? It becomes part of PublishTool's public contract, and any repository can read it.

| Option | What it means |
|---|---|
| PublishToolPrefix (Recommended) | Same word as the manifest's Prefix and the prefix key of the server settings, so one concept keeps one name everywhere. |
| PublishToolBranch | Reads naturally in a props file, but the value is the sanitized branch (wip/x becomes wip-x, DEPLOY outside git), so the name promises more than it holds. |
| PublishToolEnvironment | Names what a consumer uses it for, but the tool passes a branch prefix and does not know environments, so the name would be wrong for other repositories. |

**Answer: PublishToolPrefix (Recommended)**

### The manifest and the deploy summary already show the package prefix. Should a package additionally record which environment it was built for (change 5 of the proposal)?

| Option | What it means |
|---|---|
| No, prefix is enough (Recommended) | Nothing new is stored; the prefix stays the only identity of a package and the tool stays unaware of environments. A broken mapping in the repo is not detected by the tool. |
| Record passed properties | The manifest lists the MSBuild properties PublishTool passed. Today that is the prefix a second time, so it only pays off if a property passthrough is added later. |
| Read EnvironmentName back | One extra MSBuild evaluation per publish reads the evaluated EnvironmentName and stores it. Shows the real value and catches a broken mapping, but hard-codes a property name of one kind of project into the tool. |

**Answer: No, prefix is enough (Recommended)**

### What should deploy do when a package overwrites or deletes a config file on a target (change 2 of the proposal)?

| Option | What it means |
|---|---|
| Coded warning (Recommended) | New warning code config-changed listing the files per target. The TUI asks to accept it (default no); PublishToolCli deploy --yes stops with exit 3 unless --accept config-changed is given. Every deploy that touches a config needs a deliberate yes. |
| Informational list only | The summary and the CLI JSON list the config files, but nothing blocks. No friction for intended config changes, but an unattended run goes straight over one. |
| Nothing, drop change 2 | Only the hand-edit check (change 3) guards config files. Smallest, but a config change against an untouched server copy is not pointed out at all. |

**Answer: Coded warning (Recommended)**

### Which files should deploy treat as config files for that flag?

| Option | What it means |
|---|---|
| Fixed built-in list (Recommended) | web.config, *.exe.config, *.dll.config, appsettings.json, appsettings.*.json, matched case-insensitively on the file name. Standard .NET names, covers every file in trw.ldp's server configs; a repository with other config names is not covered until the list changes in a release. |
| Built-in plus publish setting | The same defaults plus extra patterns in the per-directory publish config, written into the manifest. Fits any repository, costs a new setting, a wizard question and CLI options. |
| Patterns in server settings | Patterns stored per prefix in server.json. The server admin decides, but servers can disagree and the person who knows the repository's files is not the one configuring them. |

**Answer: Fixed built-in list (Recommended)**

### Should the config flag also be raised for config files that are new on the target (created), or only when an existing server file is overwritten or deleted?

| Option | What it means |
|---|---|
| Overwrite and delete (Recommended) | A config that does not exist on the target cannot destroy anything, so it stays quiet; the first deploy into an empty target does not flag. |
| Create too | Every config file in a package is flagged, so the picture is complete (e.g. a first appsettings.Production.json), but a deploy into a new target always flags. |

**Answer: Overwrite and delete (Recommended)**

### Detecting hand-edited server files (change 3) needs the hash of the file a package replaces in the manifest. A manifest from today's PublishTool has no such hash, which looks the same as 'file is new'. How should the manifest format change?

| Option | What it means |
|---|---|
| Bump to format 2 (Recommended) | The new deploy reads formats 1 and 2 and skips the check for format 1 packages. An older PublishTool on the server refuses a format 2 package instead of deploying it without the check, so the server tool must be updated first. |
| Stay at 1 with a marker | Additive fields plus a manifest-level marker that hashes are present. An older server tool still deploys new packages, silently without the check, showing only today's tool-version warning. |

**Answer (Other): "Just stay at 1 without a marker. I haven't used the manifest yet, so it is fine."**

### Which files that a package overwrites should deploy compare with the expected previous content (new warning code target-modified)?

| Option | What it means |
|---|---|
| Every overwritten file (Recommended) | Catches hot-fixed views, scripts and assemblies as well as configs. Hashes each overwritten target file while planning, also in a dry run. |
| Config files only | Uses the config file patterns. Fewer hashes and less noise, but a hand edit to any other file is still overwritten silently. |

**Answer: Every overwritten file (Recommended)**

### Should files a package deletes get the same hand-edit check? The manifest lists deletions as plain paths today, so this adds the hash of each deleted file.

| Option | What it means |
|---|---|
| Overwrites and deletions (Recommended) | One rule: deploy never destroys a server-side edit without saying so. The list of deletions in the manifest changes shape, which is cheap only together with a format bump. |
| Overwrites only | Smaller manifest change. A file removed from the repository but edited on the server is deleted without a warning; it stays recoverable from the package's backup. |

**Answer: Overwrites and deletions (Recommended)**

### When the chain check already warns for a target (skipped package, no recorded deploy, unknown base), its files are expected to differ from the package's base, and SDK assemblies change with every commit. What should the hand-edit check (target-modified) do for such a target?

| Option | What it means |
|---|---|
| Run it anyway (Recommended) | One rule everywhere. The first deploy of a target lists its hand-maintained configs as the proposal intends; after a skipped package many assemblies are listed too, next to the chain warning that explains why. |
| Config files only there | With an unverified chain the check is limited to config files. Keeps the config review on a first deploy without burying it in assemblies, at the price of a second rule to explain. |
| Skip it there | No hand-edit check when the chain is not verified. No noise, but the first deploy of every target no longer flags the hand-maintained configs through this check (config-changed still does). |

**Answer: Run it anyway (Recommended)**

### The two new warnings (config-changed, target-modified) each concern a list of files per target, possibly long. How should they report the files?

| Option | What it means |
|---|---|
| Capped text, full list in JSON (Recommended) | The warning text names the first files and a count of the rest; the CLI --json result carries the complete list per warning as an additive field, so a script or agent can review every file. |
| Capped text only | Files appear only in the warning text, cut off after a few. Simplest, but nobody can get the complete list without comparing the server by hand. |
| Full list in the text | Every file in the warning text in both executables. Nothing is hidden, but a first deploy can print hundreds of lines above the confirmation prompt. |

**Answer: Capped text, full list in JSON (Recommended)**

## High-level plan

**1. The prefix reaches the build (publish side).** Every MSBuild invocation of a publish run gets the global
property `PublishToolPrefix` with the package prefix: the dependency build (restore and build) and the publish of
each project, for all three project kinds, with and without `--local`. The prefix is resolved once, before the
build. Depends on nothing, and it is the only part the published repository needs before it can produce
per-environment configs.

**2. The manifest describes what a package replaces (publish side).** The "Collect changes" step records, for
every changed file, the hash of the file it replaces in the previous reference, and for every deleted file the hash
of the file that is deleted. Depends on nothing; can run in parallel with 1.

**3. Two new deploy checks (server side).** `config-changed` flags config files a package overwrites or deletes
on a target; it needs nothing from 1 or 2. `target-modified` flags files whose content on the server is not what
the package expects to replace or delete; it depends on 2. Both are coded warnings of the plan, so they appear in a
dry run and stop nothing by themselves.

**4. Reporting.** The TUI summary and the CLI text output show the capped warning text; the CLI JSON result carries
the complete file list per warning. The help text that lists the warning codes and the repository instructions
(publish command line, manifest, warning codes, the deploy preflight description) change with each phase, not at
the end.

**5. Hand-back to the published repository.** The property name and the two warning codes are the contract the
trw.ldp session builds on. That work is outside this repository.

## Architecture decisions

**The tool passes a fact, never a meaning.** PublishTool passes the prefix it already computes for the package
name and knows nothing about environments, transforms or any one repository. Mapping `uat` to an environment is
the published repository's business and is versioned there. Reason: the user's requirement that the tool stays
usable for any repository, and a mapping in per-machine config would silently publish development configs on a
machine that lacks it.

**One prefix per run, resolved before the build.** Today the prefix is resolved inside the deploy preparation.
It becomes an input of the whole run: the same value goes to the build, the publishes, the package name and the
manifest. The property is always passed, including the `DEPLOY` fallback outside a git repository and the
sanitized name of any other branch, so a consumer must treat unknown values as "no environment". A test has to
enforce that the value MSBuild receives equals the manifest's `Prefix`, for every project kind and for the
dependency build, and that a prefix with characters special to MSBuild or the command line (`;`, `=`, `&`, `%`,
`$`, quotes) either arrives unchanged or fails the run before anything is built.

**Same global properties in the dependency build and the publishes.** A publish runs with
`BuildProjectReferences=false` and reuses what the dependency build left behind. If only the publishes got the
prefix, outputs built without it (referenced projects, intermediate files) would end up in an environment publish.
Both get it, so everything that lands in the publish output was built with the same prefix.

**Previous hashes live in the manifest, computed locally.** The publish machine already holds the extracted
previous reference while it collects changes, so the hash of a replaced or deleted file costs one local read and no
IO on the share. A file that is new in the publish has no previous hash. Deletions become entries that carry the
path and the hash of the deleted file.

**Manifest format stays 1 and changes in place.** The user has not deployed a manifest package yet, so there is
nothing to stay compatible with: no format bump, no marker. The consequence is deliberate: a package written by a
build from before this change is not a supported input of the new deploy (publish again), and an older tool is not
a supported reader of new packages. The first manifest that is used on a real server is the baseline for all later
compatibility; from then on a shape change means a new format.

**The hand-edit rule.** For a file the plan overwrites: its content on the target must equal the previous hash or
the new hash, otherwise it is reported. Equal to the new hash covers deploying a package again. A file without a
previous hash that already exists on the target with other content is reported too. For a file the plan deletes:
its content on the target must equal the recorded hash. A file the package expects but the target does not have is
not reported, because nothing is destroyed. The rule is the same for every file and every target; it is not
relaxed when the chain check warns or for full publishes, where every existing file that differs is listed next to
the chain warning that explains it.

**Config files are a fixed list in the core.** `web.config`, `*.exe.config`, `*.dll.config`, `appsettings.json`
and `appsettings.*.json`, matched case-insensitively on the file name. These are .NET conventions, not knowledge of
one repository. `config-changed` is raised only for files the plan overwrites or deletes, never for created ones.
The two checks are independent: one file can appear in both warnings.

**Warnings carry their files.** A warning is a code and a message today. The two new warnings are one per target
and code, with the file list attached as data. The text is built from the first files plus a count; the CLI JSON
exposes the full list as an additive field, so the schema version does not change. `config-changed` and
`target-modified` are new public codes: they join the list `--accept` validates against and the CLI help, and are
never renamed. They belong to `deploy` only; `rollback` is unchanged.

**The checks run while planning.** Hashing target files happens in the preflight, before the backup and before
any host is stopped, and also in a dry run. Nothing on the server changes because of a check.

## Weaknesses and risks

| Risk | Likelihood / cost | What reduces it |
|---|---|---|
| MSBuild's incremental build does not treat a changed global property as a changed input. Publishing `uat` and then `prod` from one checkout can reuse an intermediate transformed config of the other prefix | Medium, because switching branches in one checkout is the normal workflow; a prod package with a UAT config | The published repository's transform targets must rerun on a changed prefix; on the server `config-changed` and `target-modified` show the file before it is overwritten |
| The mapping in the published repository is missing or broken, so a `uat` publish contains development configs. The tool cannot see this (recording the environment was declined) | Low to medium; outage of the environment after a deploy | `config-changed` lists every config the package touches; review them on the first deploys |
| A prefix with `;`, `=`, `&` or `%` (legal in git branch names, not removed by the sanitizer) breaks or alters the MSBuild property list | Low; a failed build or a wrong property value | The escaping invariant and its test above |
| A package from before this change deployed with the new tool: every overwritten file has no previous hash and is reported as modified, or the manifest does not parse at all | Low, no such package is in use; one confusing dry run | Publish again; `tool-version` already warns on any version mismatch |
| First deploy of each target after trw.ldp produces configs: every config is in the package, so both new warnings fire for each target | Certain, once per target | Intended review point; the JSON result holds the complete lists |
| After a skipped package or on a target without a record, most assemblies are listed as modified and bury the configs in the text | Medium; a long list where only a few entries matter | Decided to accept; `config-changed` names the configs separately, the chain warning explains the rest |
| `config-changed` stops every unattended `--yes` deploy that touches a config | Certain; one more code to pass | Intended; `--accept config-changed` after a dry run |
| A hand edit to a file that did not change in the repository stays invisible, because a package only holds changed files | Certain by design; the edit survives, so nothing is lost | A drift report over the full publish is a separate feature |
| The TUI shows only the capped text, the complete list exists only in the CLI JSON | Low; a second command to see everything | `PublishToolCli deploy --dry-run --json` on the same package |
| Planning now reads and hashes target files; a dry run without administrator rights may not be allowed to read some | Low; an incomplete check | Open, see below |
| Reference zips on the share and the local publish and deploy directories now contain environment configs | Certain; whatever the published repository puts into them is readable there | Encrypted sections are committed as ciphertext by trw.ldp; nothing for the tool to do |
| Another repository uses config file names outside the fixed list | Low today; those files are not flagged by `config-changed` | `target-modified` still covers them; patterns can become configurable later |

Open questions:

- What a target file that cannot be read during planning does: fail the plan with an error, or report the file
  under `target-modified` as "could not be compared". To be settled during implementation; it must not be silently
  treated as unmodified.
- How many files the warning text names before the count. A presentation detail, the TUI caps other lists at 10.

## Out of scope

- A generic `--property key=value` passthrough (change 4 of the proposal) and post-deploy hooks (change 6).
- Recording or showing the environment of a package (change 5): declined, the prefix is the identity of a package.
- Running config transforms in PublishTool, filtering environment files out of a publish, or any other knowledge of
  the published repository. The mapping from prefix to environment lives in that repository.
- Configurable config file patterns, in the publish config or in the server settings.
- A manifest format bump, a compatibility marker, and deploying packages written before this change.
- An option to override the prefix for a run.
- Detecting hand edits to files a package does not contain (drift between the server and the full publish).
- A complete file list in the TUI.
- Changes to `rollback`.
