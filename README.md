# PublishTool

## TW: AI, Claude, vibe coding

This tool is horrendously vibe-coded. Don't use it or look at the code. You will regret becoming a programmer.

## Using it from Claude

[skills/publishtool/SKILL.md](skills/publishtool/SKILL.md) is a Claude skill that teaches Claude Code how to use
`PublishToolCli` (the non-interactive CLI): publish, dry run before every deploy, which warnings to show you, and what
it must never do without asking. It also tells Claude how to install `PublishToolCli` from the
[releases](https://github.com/dalibor-osu/PublishTool/releases) when it is missing.

Install the skill for yourself (all repositories) from a clone of this repository:

```powershell
New-Item -ItemType Directory -Force "$HOME\.claude\skills\publishtool" | Out-Null; Copy-Item skills\publishtool\SKILL.md "$HOME\.claude\skills\publishtool" -Force
```

Or for one repository only, copy `SKILL.md` to `.claude\skills\publishtool\` inside that repository and commit it.
Run the command again after updating PublishTool to pick up changes of the skill.