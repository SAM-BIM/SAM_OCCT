# Q4 agent continuity pilot — 2026-10-12

Status: implemented on `docs/agent-continuity-pilot-q4`; owner review required.
Base: `sow/2026-Q4` at `e27242931e7007224c567933827f96ef137e0807`.
PR: [SAM_OCCT#74](https://github.com/SAM-BIM/SAM_OCCT/pull/74).
Companion: [SAM#191](https://github.com/SAM-BIM/SAM/pull/191).
Locally validated instruction/content head: `8cb1d37cdb6ca165e84ccaee1d044e0534bc4aa4`.
The final PR head is recorded in the live PR description because committing that
SHA into this file would create another head. This record contains the immutable
tested content SHA and commands; only this record changes afterward. The final
docs diff is checked separately, and the post-merge closeout records the merge SHA.

## Problem and implementation

CLAUDE.md contains useful OCCT-specific testing/signature conventions, but does
not import AGENTS.md. Claude's default file selection can therefore omit Q4
branch and continuity instructions. Add `@AGENTS.md` and preserve every existing
Claude-specific convention. No duplicate Cursor/Hermes rules are added.

AGENTS.md now directs heading-first/task-specific progress reads, verifies old
status against Git/live PRs, persists SAM-BIM-only operations without rewriting
provenance/integrations, and identifies portable handoff evidence. Preserve Q3,
internal-files-off-master, owner-reviewed feature PRs and post-merge-only Q4
progress closeouts; explicitly retain `[skip ci]` for those closeouts.

Files: AGENTS.md, CLAUDE.md and this record only. PROJECT_PROGRESS.md is unchanged.
Companion SAM pilot record: `SAM-BIM/SAM`, same branch name,
`documentation/Agent-Continuity-Pilot-Q4.md` (organisation findings and sources).

## Evidence and validation

Review disposition: Copilot/Kimi's reproducibility concern is addressed with the
tested SHA and commands below; AGENTS.md clarifies the record-only final commit.
The master-policy contradiction was real: master already contains general
CLAUDE.md guidance (blob `14a41e63703bdf41a34bc9ab502ccc0844452dc8`). Preserve it.
Quarter promotion must use the approved master CLAUDE.md baseline, excluding the
Q4 import, rather than deleting the legacy file. Master has no AGENTS.md or
PROJECT_PROGRESS.md. No master file is changed by this PR.

Run from this PR checkout (PowerShell):

```powershell
$baseRevision = 'e27242931e7007224c567933827f96ef137e0807'
$testedRevision = '8cb1d37cdb6ca165e84ccaee1d044e0534bc4aa4'
git diff --check $baseRevision $testedRevision
git diff --name-only $baseRevision $testedRevision
git diff --quiet $baseRevision $testedRevision -- PROJECT_PROGRESS.md
git diff --check $baseRevision HEAD
git diff --name-only $baseRevision HEAD
git diff --quiet $baseRevision HEAD -- PROJECT_PROGRESS.md
git diff --name-only $testedRevision HEAD
```

Results: both whitespace/progress checks exit 0; both full-PR lists contain only
AGENTS.md, CLAUDE.md and this record; tested-to-final lists only this record.
Local docs validation is distinct from final-head hosted checks and product tests.

43 in-scope local checkouts and live SAM-BIM remotes audited (Solver_Upstream
excluded); reviewed continuity blobs match live Q4/default branches. The attached
SAM examples equal the local files. OCCT is selected specifically because of its
existing standalone CLAUDE.md, not for mechanical template rollout.

Normalize CRLF to LF; read all level 1–3 progress headings plus the leading icon
closeout up to `## Branch`: 16,547 to 2,317 characters (86.0% less read context).
This example suits an icon follow-up; other tasks require their relevant sections.
It is not a measured token, cost, latency or client-behavior benchmark.

Local diff/whitespace checks pass; only these three docs files change. All original
CLAUDE.md text survives unchanged; no product, test, workflow, provenance or legacy
reference change. No build/native integration tests run for this docs-only change.
Hosted CI results are the live PR's final-head results, distinct from these local checks.

[Claude's official memory documentation](https://code.claude.com/docs/en/memory)
supports the import, including Windows portability. Current versions can also
load AGENTS.md directly when no CLAUDE.md in/above the project overrides that
default. Check actual installed-client context; imports do not imply that Cursor
or Hermes preprocess Claude syntax (they already support AGENTS.md).

## Risks and next step

Owner-requested follow-up: AGENTS.md now specifies reviewed product promotion
excluding quarterly agent files/this pilot record while preserving legacy master
CLAUDE.md, Q4 closeouts before freezing,
and an explicitly authorized Q1 bootstrap carrying useful instructions/evidence.
No release, freeze or Q1 branch creation happens in this pilot. Follow-up changes
only AGENTS.md and this record; whitespace/scope checks pass, with progress and
existing Claude-specific instructions unchanged.

The stale bootstrap status remains historical text until an authorized post-merge
closeout labels it; no progress edits on this PR branch. Extra stable instruction
text is a small overhead; task-specific reads are the intended efficiency gain.

Next: owner reviews this pilot and the SAM companion; verify instruction loading
in fresh Claude/Codex/Cursor/Hermes sessions and measure input/cache tokens and
handoff correctness before rollout. After an approved merge only, write a known-SHA
Q4 progress closeout as docs-only `[skip ci]`. Do not merge before owner/code-owner
approval, resolution of open review findings and green final-head checks. No broad cleanup.

Generated by Michal Dengusiak & Claude Code
