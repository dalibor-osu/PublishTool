# Non-interactive CLI

Status: agreed
Date: 2026-11-24
Area: new core library and new PublishToolCli executable; every existing command (publish, config, deploy,
rollback, update), Program.cs, configuration, release workflow and self-update

## Context of the change

PublishTool is a single Native AOT executable whose commands are built around Spectre.Console prompts and a
redrawn progress table (see "UI model" and "Usage guide" in `CLAUDE.md`). Some commands have partial
non-interactive switches, but no command can be run end to end from arguments alone:

| Command | What blocks unattended use today |
|---|---|
| `publish` | Always runs in the alternate screen and waits for a key at the end, even with `-a`. A project subset or the Debug configuration can only be chosen in prompts (`-a` means all publishable projects in Release). An incomplete config starts the wizard (`ConfigHandler.Load`). The package name and path are only shown in the progress table, so a caller cannot pass them to `deploy`. |
| `config` | Wizard only (`ConfigHandler.RunWizard`). |
| `deploy` | `-y` skips the confirmation, but missing target mappings (`ServerConfigHandler.ResolveTargets`) can only be entered in a wizard and warnings can never be accepted, by the "Stop on warnings" decision of `docs/specs/2026-11-23-server-deploy-command.md`. |
| `rollback` | Same as `deploy`: `-y` exists, warnings stop the run. |
| `update` | A user on a pre-release is asked whether to include pre-releases (skipped with `-pre`), and a downgrade always asks with no switch to skip it. `-v/--version` is parsed but ignored (known gap in `CLAUDE.md`). |

Across all commands: `Program.cs` returns 0 on Ctrl+C and 1 on every failure, unexpected exceptions only go to
the log file, and all output is Spectre markup and tables with no machine-readable form.

Release and update are tied to one executable: `.github/workflows/release.yml` builds `PublishTool` for six RIDs
and `UpdateCommand` looks for exactly `PublishTool(.exe)` in the downloaded archive and swaps it in place.

The trigger for the change is automation: scripts and AI agents need to drive the tool with one command per
operation, get a parseable result and a meaningful exit code, and never block on a prompt.

## User request

Add full CLI support so that everything can be done with a single command, without the dynamic TUI. The idea
is another project, PublishToolCli, with similar functionality, where everything a command needs can be given as
command line arguments. The main use case is automation and AI agents being able to use the tool. The CLI is a
separate executable built on a shared core library, independent of the original app: a user who only wants the
CLI must be able to have only the CLI.

## Decisions

### Which commands must the non-interactive CLI cover in this change?

| Option | What it means |
|---|---|
| All commands (Recommended) | publish, config, deploy, rollback, update all fully scriptable; one consistent contract, biggest change. |
| Dev side only | publish and config only; covers the agent-on-dev-machine case, server deploy keeps its current -y behaviour. |
| Dev side + deploy | publish, config, deploy and rollback; update stays interactive since agents rarely need to self-update. |

**Answer: All commands (Recommended)**

### Where should the non-interactive CLI live?

| Option | What it means |
|---|---|
| Mode in same exe (Recommended) | One binary with a non-interactive switch that turns every prompt into an argument or a failure; release, update and config untouched, but commands gain a second code path. |
| PublishToolCli + core lib | Extract a Spectre-free core library, two thin exes on top; cleanest split, but a large refactor plus a second exe in release assets and self-update. |
| PublishToolCli, shared src | Second exe project that compiles the existing source files plus its own commands; less refactor, but duplicated commands and update must handle two exes. |

**Answer: "Let's do PublishToolCli + core lib, so a separate exe. It should be independent of the original app. If user only wants CLI, we should allow that."**

### How should PublishToolCli be released and versioned?

| Option | What it means |
|---|---|
| Same release, own assets (Recommended) | One workflow and one version tag; each release carries PublishTool-v<v>-<rid> and PublishToolCli-v<v>-<rid> archives, so either can be installed alone; both always share a version. |
| Separate release lines | Own tags (e.g. cli-v1.2.0) and own workflow; fully independent cadence, but two changelogs and two semver streams. |
| One archive, both exes | Simplest release change; but a CLI-only user still downloads the TUI exe. |

**Answer: Same release, own assets (Recommended)**

### How should PublishToolCli update itself?

| Option | What it means |
|---|---|
| Own update command (Recommended) | PublishToolCli update replaces only PublishToolCli from its own asset; the two installs never touch each other. |
| Updates both if present | Either exe's update also refreshes the other one next to it; keeps versions in step, but couples the installs. |
| No self-update | CLI is updated by whoever installed it (script, package manager); smallest scope, agents never replace binaries. |

**Answer: Own update command (Recommended)**

### How should the CLI get settings like publish/shared/deploy directories and server deploy targets?

| Option | What it means |
|---|---|
| Stored + overrides (Recommended) | Uses the stored config, any value can be overridden per run by an argument, and a config command sets values non-interactively; missing values fail with the names of the missing keys. |
| Arguments only | Stateless: every run passes every path and target; nothing hidden, but long command lines and no remembered project list or deploy records. |
| Stored only, set by CLI | Values only come from the stored config, edited by a config set command; short runs, but a one-off different path needs a config change. |

**Answer: Stored + overrides (Recommended)**

### Should PublishToolCli share the config files with the TUI app?

| Option | What it means |
|---|---|
| Share the files (Recommended) | Same config.json and server.json; configure once in either tool, and deploy records stay consistent whichever exe deployed. |
| Own files | PublishToolCli keeps separate files; fully independent, but a machine with both is configured twice and the deploy chain check sees two histories. |

**Answer: Share the files (Recommended)**

### What should PublishToolCli print?

| Option | What it means |
|---|---|
| JSON stdout, log stderr (Recommended) | Exactly one JSON result object on stdout (package name/path, files, warnings, errors); plain progress lines on stderr; trivially parseable, less pleasant for a human reading stdout. |
| Text default, --json flag | Readable plain lines by default, the JSON result only with --json; nicer by hand, but agents must remember the flag. |
| Plain text only | Line-oriented text with stable key: value result lines; no JSON contract to version, but parsing is fragile. |

**Answer: Text default, --json flag**

### Which exit code scheme should PublishToolCli use?

| Option | What it means |
|---|---|
| Distinct codes (Recommended) | e.g. 0 ok, 1 failed, 2 invalid arguments or missing config, 3 stopped on unaccepted warnings, 130 cancelled; a caller can react without parsing, but the table must stay stable. |
| 0 / 1 only | Same as the TUI today; simple, but the reason is only in the output. |

**Answer: Distinct codes (Recommended)**

### How should PublishToolCli deploy/rollback handle warnings (version mismatch, chain mismatch, missing target dir, newer deploy before rollback)?

| Option | What it means |
|---|---|
| Accept by kind (Recommended) | Each warning has a stable code; --accept <code>[,<code>] lets exactly those through, anything else stops with the warnings exit code; explicit, an agent cannot wave through something it never saw. |
| Blanket accept flag | A single --accept-warnings lets every warning through; simple, but one flag silences checks added later too. |
| Never accept | Keep the 2026-11-23 decision: any warning stops the CLI and a human resolves it in the TUI; safest, but the first deploy after an upgrade is never unattended. |

**Answer: Accept by kind (Recommended)**

### What happens to the TUI app's own non-interactive bits (publish -a, deploy/rollback -y)?

| Option | What it means |
|---|---|
| Keep as is (Recommended) | TUI behaviour and flags stay unchanged; existing scripts keep working, at the cost of two ways to script deploy. |
| Remove, point to CLI | TUI becomes purely interactive and its help points to PublishToolCli; one scripting path, but breaks existing -y/-a usage. |

**Answer: Remove, point to CLI**

### What progress should PublishToolCli print while long work (MSBuild, upload, deploy) runs?

| Option | What it means |
|---|---|
| Phase lines (Recommended) | One line when each step starts and finishes (with its detail, e.g. '12 files changed'), full MSBuild output only in the log file or with --verbose; short enough for an agent's context window. |
| Full streamed output | Every MSBuild and step line as it happens; nothing hidden, but thousands of lines flood an agent's context. |
| Silent until done | Only the final result; cleanest, but a long publish looks hung. |

**Answer: Phase lines (Recommended)**

### Which of these are explicitly deferred to a later change?

Multiple choice.

| Option | What it means |
|---|---|
| MCP server mode | Exposing the commands as MCP tools for agents; the CLI with --json covers agents first. |
| Fixing update --version | Installing an exact version (the known gap); stays unimplemented in both exes for now. |
| Editing existing server targets | Changing or removing an already mapped target via config; only adding missing ones is in scope. |

**Answer: MCP server mode**

### Should PublishToolCli deploy and rollback need an explicit go-ahead flag to change the server?

| Option | What it means |
|---|---|
| Require --yes (Recommended) | Without --yes the command stops after the plan like --dry-run (exit 0) or fails (exit 2); a mistyped or exploratory call by an agent never touches IIS and services. |
| Run directly | A bare deploy/rollback runs once preflight passes and warnings are accepted; one flag less, but any invocation changes the server. |

**Answer: Require --yes (Recommended)**

### Should a deploy whose automatic restore failed (manual rollback needed) get its own exit code?

| Option | What it means |
|---|---|
| Own exit code (Recommended) | e.g. 4: the server is in a broken state and the caller must run rollback; distinguishable without parsing output, one more code in the table. |
| Plain failure (1) | Same as any failed deploy; the restore state is only in the text/JSON output, which a caller must read. |

**Answer: Own exit code (Recommended)**

### What should deploy and rollback do without --yes?

| Option | What it means |
|---|---|
| Stop after the plan like --dry-run (exit 0) | The command verifies and prints what it would do, then exits without changing anything. |
| Fail (exit 2) | The command refuses to run without the go-ahead and reports invalid arguments. |

**Answer: "Deploy without --yes should run as a dry run"**

### In --json mode, should progress lines go to stderr so stdout carries only the JSON result?

| Option | What it means |
|---|---|
| Yes (proposal) | stdout holds exactly the JSON result and stays parseable; progress is still visible on stderr. |

**Answer: "I'm ok with that"**

### Should the JSON result carry its own schema version, independent of the tool version, with only additive changes within one schema version?

| Option | What it means |
|---|---|
| Yes (proposal) | Callers detect breaking changes of the result shape without tracking tool releases. |

**Answer: "I'm ok with that"**

### How should the CLI release assets be named so already-installed TUIs never update themselves with the CLI exe?

Asked during implementation: TUI versions released before this change pick the first asset whose name ends in
`<os>-<arch>.<ext>` and fall back to the only non-LICENSE file in the archive, so the agreed name
`PublishToolCli-v<v>-<rid>` could make them install the CLI over `PublishTool.exe`.

| Option | What it means |
|---|---|
| PublishToolCli-<rid>-v<v> (Recommended) | e.g. PublishToolCli-win-x64-v1.4.0.zip; old TUIs never match it, upgrades keep working; deviates from the agreed name order. |
| Agreed name + decoy file | Keep PublishToolCli-v<v>-<rid>; add a second file to CLI archives so an old TUI that picks it fails safely instead of overwriting itself, but it may then fail to update once. |
| Agreed name, accept risk | Keep naming as agreed and upload TUI assets first; usually works, but an old TUI can still replace itself with the CLI. |

**Answer: PublishToolCli-<rid>-v<v> (Recommended)** - this replaces the CLI asset name of the release decision above.

## High-level plan

**1. Core library.** Everything that is not presentation moves into a new library project without a Spectre
dependency: configuration (models, stores, version registries, migrations), project scanning, the MSBuild
invocation, the publish and deploy-preparation workflow, package manifest and lineage, deploy planning, the deploy
and rollback runners, host control, GitHub release lookup and the self-update mechanics (including installing an
exact version). The core never prompts and never writes to the console: it reports progress through a seam
(see Architecture decisions), takes every choice as an input and returns warnings and errors as data. Deploy and
rollback warnings get stable codes here. This phase is behaviour-preserving and comes first; everything else
depends on it.

**2. TUI on the core.** PublishTool is rewired onto the core with the same prompts, wizards and progress table.
Its non-interactive switches are removed (`publish -a`, `deploy -y`, `rollback -y`), its help points to
PublishToolCli for scripted use, and its `update` gains exact-version install through the core. Depends on 1;
can run alongside 3.

**3. PublishToolCli executable.** A second Native AOT console app with the commands `publish`, `config`,
`deploy`, `rollback`, `update`, `version` and `help`. Every input is an argument, stored config supplies defaults
that arguments override per run, and nothing ever prompts or waits for a key. Plain text output by default, one
JSON result with `--json`, phase lines as progress, distinct exit codes, `--accept <code>` for deploy and
rollback warnings, `--yes` as the required go-ahead before deploy or rollback change the server, and a
non-interactive `config` that reads, sets and removes per-directory settings and server targets (including
changing and removing already mapped targets). Depends on 1.

**4. Release and self-update.** The release workflow builds both executables for every RID and attaches
separate archives under one version tag. Each executable's `update` downloads and replaces only itself from its
own asset. Depends on 1 and 3.

**5. Documentation.** The project's agent and developer docs (layout, commands, conventions, usage guide), the
end-to-end test instructions (which use `publish -a` today and move to the CLI), and a note in the 2026-11-23
deploy spec that its `-y` design is superseded. Runs with every phase, finishes last.

## Architecture decisions

**A core library with two thin front ends.** Chosen by the user over a non-interactive mode in the same
executable, so that the CLI is independent and can be installed alone. The core is AOT and trim compatible
(source-generated JSON, `[GeneratedRegex]`, no reflection), because both front ends publish with Native AOT.
Commands and their options stay in the executables; the core has no notion of a command line.

**One command parser generator for both executables.** The existing source generator is referenced by both
front ends and emits each one's parser and help from the commands in that project. Today it hard-codes the
attribute names, the command interface, the result type, the help command and the application name "PublishTool"
in the help text. The attributes, the command interface and the result type move to a place both executables
reference, and the application name (and the help command) is taken per project instead of being fixed, so
`PublishToolCli --help` names itself. Keeping one generator means one set of PT diagnostics and identical option
syntax in both tools.

**Progress seam instead of `JobRow`.** Long-running core work reports through an abstraction of steps (started,
renamed, output line, finished with state and detail). The TUI adapts it to `JobDisplay` rows; the CLI turns it
into one line per step start and finish, sends output lines to the log file and prints them only with
`--verbose`. This is what lets the CLI avoid both the redrawn table and an agent-flooding MSBuild stream.

**Choices are inputs, decisions are the caller's.** Project selection, build configuration, target mappings and
the acceptance of warnings are parameters or return values of the core, never prompts inside it. The TUI fills
them from prompts and wizards, the CLI from arguments and stored config. A missing required value in the CLI is
an error naming the missing setting, not a prompt.

**Stable warning codes.** Every deploy and rollback warning carries a kebab-case code (for example a tool
version mismatch, a chain mismatch, an unknown base, a target without a record, a package already deployed, a
full publish onto a deployed target, a missing target directory, a newer deploy before a rollback). The CLI's
`--accept` takes a list of codes; a warning whose code is not listed stops the run before anything changes. Codes
are part of the CLI's public contract: renaming one is a breaking change, a new check gets a new code and is
therefore never accepted by an existing script.

**Shared config files, one owner of the format.** Both executables read and write the same
`%APPDATA%\PublishTool\config.json` and `%ProgramData%\PublishTool\server.json`, through the same stores and
version registries in the core. Values given as CLI arguments override the stored ones for that run only and
are never saved as settings; only the CLI's `config` command (or the TUI wizard) changes them. The exception is
state a run produces: a deploy to an overridden target still writes that target's `Deployed` record and its
journal, because the chain check and rollback depend on them. Both executables share
one version number, so a matching pair always agrees on the format and on the tool version stamped into
packages.

**Output contract.** Text output is for people and may change. `--json` prints exactly one JSON object on stdout
at the end of the run, carrying a schema version, the command, the outcome and exit code, the command's result
(for `publish` at least the package name and path and per-project change counts; for `deploy` the per-target
plan and outcome) and the lists of warnings (code and message) and errors. In `--json` mode every progress line
goes to stderr, so stdout carries only the JSON result. The result carries its own schema version, independent
of the tool version, and changes within one schema version are additive only.

**Exit codes.** Distinct and documented: 0 success, 1 the operation failed, 2 invalid arguments or missing
configuration, 3 stopped on warnings that were not accepted, 4 a deploy failed and its automatic restore failed
too (journal `RestoreFailed`, the caller must run `rollback`), 130 cancelled with Ctrl+C. The TUI keeps 0/1. The
existing Ctrl+C behaviour inside a `CriticalSection` (a second Ctrl+C does not kill a deploy or rollback) holds
in both executables.

**`--yes` is the go-ahead, warnings are separate.** In the CLI, `deploy` and `rollback` change the server only
with `--yes`, and only after every warning was accepted by code. Without `--yes` they run as a dry run: verify,
print the plan (and for `--json` the result) and exit 0 without changing anything. The two are independent:
`--yes` never accepts a warning, `--accept` never replaces `--yes`.

**The 2026-11-23 `-y` design is superseded.** That spec gave the TUI's `deploy` and `rollback` a `-y` switch that
skips the confirmation, prints without the alternate screen and stops on any warning. With this change the TUI
loses `-y` entirely and becomes interactive only; unattended deploys belong to the CLI, whose `--yes` plus
`--accept` replace that design. `publish -a` goes the same way.

**Separate release assets, separate self-update.** One workflow and tag produce `PublishTool-v<v>-<rid>` and
`PublishToolCli-<rid>-v<v>` archives (the RID comes first so older TUIs never match a CLI asset, see the asset name
decision). The asset naming, the GitHub download URL and the executable lookup in
`update` become parameters of the core's update logic, so each front end passes its own name and never touches
the other executable.

## Weaknesses and risks

| Risk | Likelihood | Cost | What reduces it |
|---|---|---|---|
| The core extraction touches almost every file and the repo has no automated tests, so the TUI can regress silently (rollback on failure, Ctrl+C handling, the deploy chain checks). | High | High: deploy and rollback act on production servers. | Keep phase 1 strictly behaviour-preserving and land it before any CLI work; rerun the elevated end-to-end test in `TestApps` before and after; consider a small test project for the core now that it has a clean seam. |
| An agent passes every known warning code to `--accept` by habit, which turns "accept by kind" into a blanket accept. | Medium | High | Document that `--accept` should list only codes seen in a `--dry-run` of the same package; recommend dry-run first in the CLI help. |
| Removing `publish -a` and `deploy/rollback -y` from the TUI breaks existing scripts and the `TestApps` instructions. | Certain for those users | Low to medium | Release it as a breaking change (`feat!`), print a pointer to PublishToolCli when a removed switch is used, update `TestApps`. |
| Two processes (both executables, or parallel agent runs) save the same config file at the same time and one update is lost. | Low today, higher with agents | Medium: a lost target mapping or deploy record breaks the chain check. | Re-read before write and write via temp file and move; consider a file lock around read-modify-write. |
| Mixed versions installed (CLI updated, TUI not): the older one rejects a config file migrated by the newer one, and a deploy by the other executable raises the tool-version warning. | Medium | Low | Same version for both assets; the config error already says what to do; document updating both. |
| The JSON result and the exit code table become a contract agents depend on. | Certain | Medium on every change | Document the exit codes; version the JSON schema (see the open questions). |
| A deploy can now run with no human looking at the summary. | Medium | High | `--dry-run` stays, `--yes` is required, warnings still stop unless accepted by code, the server-side admin requirement stays. |
| The generator change (attributes and application name no longer fixed) breaks the existing TUI parser or help. | Low | Medium: the tool does not start. | Compare the emitted parser and help of the TUI before and after; the PT diagnostics still guard declarations. |

Open questions, to be settled during implementation:

- The exact option names of each CLI command and of `config` (settings and server targets), the final list of
  warning codes and the JSON field names. They follow from the decisions above and are fixed in the
  implementation notes.
- What the CLI returns when `deploy` or `rollback` run without administrator rights (exit 1 or 2).

## Out of scope

- An MCP server mode exposing the commands as agent tools (deferred by the user).
- Any change to what publish, the deploy preparation, deploy or rollback actually do to files, shares and hosts;
  only how they are driven and reported changes.
- Deploy and rollback on other platforms than Windows, and running them without administrator rights.
- Interactive features in PublishToolCli: it never prompts, never uses the alternate screen and never waits for
  a key.
- A separate config format or location for the CLI.
- Merging the two executables back into one, or making one install the other.
