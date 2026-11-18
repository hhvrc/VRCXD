@AGENTS.md

## Claude Code

- `server/`, `client/` and `agent/` each have a `CLAUDE.md` that imports that part's `AGENTS.md`;
  Claude Code loads it once you read or edit a file there.
- Skills in `.claude/skills/` hold the step-by-step workflows: `port-upstream`,
  `upstream-regression-test`, `add-mcp-tool`, `add-vrchat-endpoint`, `add-ef-migration`,
  `client-i18n-string`, and `add-live-topic` (a placeholder until topics exist).
- Subagents in `.claude/agents/`: `upstream-auditor` (triages upstream commits or items, read-only),
  `test-writer` (writes tests for a change) and `reviewer` (checks a diff against these rules).
- Shell `cd` does not persist between calls in subagents; use absolute paths or `cd dir && ...`.
- Other sessions may share the machine. Run one test class while iterating and the full suites
  only when asked or at the end.
- Personal notes belong in `CLAUDE.local.md` (git-ignored), not here.
