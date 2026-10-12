# Agent instructions rollout — Q4 (2026-10-12)

Status: implemented on `docs/agent-instructions-rollout-q4`; owner review required. Not merged.
Kind: Follow-up to the merged pilot.
Base: `sow/2026-Q4` at `c825a406606d2ac0a456431268eeaae538014951`.
PR: the live PR for this branch in `SAM-BIM/SAM_OCCT` (its number is not committed here).
Tested content SHA: `d7abc9be9ab2f5864335448f28d53cd81ff3b0ca` (changes only AGENTS.md).
Final PR head and hosted check results: recorded in the live PR description. A record
cannot contain its own commit SHA, so this file is the only change after the tested SHA.
Baseline: SAM-BIM/SAM#191 (merge `02bbafb8`, closeout `46e96b72`) and SAM-BIM/SAM_OCCT#74
(merge `3d0a4725`, closeout `c825a40`).

## Scope

Documentation only. No product code, build, workflow, dependency pin or tool changed.
`PROJECT_PROGRESS.md` is not changed on this branch; its closeout follows merge.

## Changes

- `AGENTS.md`: adds the agreed merge conditions, the guarded post-merge branch-cleanup rule (including dependency-branch resolution), the model/effort handoff rule, a release/promotion approval line and `preserve existing local changes`; generalizes the promotion-exclusion list to `Agent-Continuity-*-Q4.md`. Pilot text is otherwise unchanged. `CLAUDE.md` is not touched.

Shared rules added or confirmed in `AGENTS.md`: Git and committed records are authoritative; SAM-BIM-only
operations with validated URLs; heading-first, task-specific progress reads; feature branch + owner-reviewed PR
for everything except the post-merge `[skip ci]` progress closeout; tested-SHA, local-versus-hosted,
dependency-order and remaining-gap evidence; merge only after owner approval, final-head checks and resolved
findings; guarded post-merge cleanup of the task's branches (expected-SHA check, full-merge check, no open PR,
lease-guarded delete, no worktree deletion); frozen quarters; internal agent files stay off master promotions;
model/effort/session/mode on every recommendation.

## Preserved repository-specific rules

- OCCT's existing `CLAUDE.md` (testing method in `TESTING.md`, xUnit naming, SPDX headers, signature convention) is unchanged, and master's legacy `CLAUDE.md` must be preserved without the Q4 import at promotion. AGENTS.md now points at that file for OCCT conventions.

## Validation

Local, documentation checks only (no product build or test was run, none applies):

```bash
base=c825a406606d2ac0a456431268eeaae538014951
tested=d7abc9be9ab2f5864335448f28d53cd81ff3b0ca
final=$(git rev-parse HEAD)   # must equal the final head in the PR description
for revision in "$tested" "$final"; do
  git diff --check "$base" "$revision"; echo "check $revision exit=$?"
  git diff --name-only "$base" "$revision"
  git diff --quiet "$base" "$revision" -- PROJECT_PROGRESS.md; echo "progress $revision exit=$?"
done
git diff --name-only "$tested" "$final"
git show "$final":CLAUDE.md | grep -n '^@AGENTS.md'
```

Expected: every `exit=` is 0; the full-PR file lists contain only AGENTS.md and this record
(at the tested SHA only AGENTS.md); the tested-to-final list contains only this record; the import
line is found. PowerShell works with `git rev-parse HEAD` and `if ($LASTEXITCODE -ne 0)` checks.

Result at the tested SHA (2026-10-12, Windows, Git for Windows): `git diff --check` exit=0;
`PROJECT_PROGRESS.md` unchanged exit=0; changed files: AGENTS.md; no CR, no BOM, final newline;
no unreplaced template tokens; `sow/2026-Q4` active, `sow/2026-Q3` frozen, no `sow/2026-Q2` reference.
The same checks at the final head and the hosted PR checks are reported in the PR description;
they are not asserted here. No agent-client token/behaviour benchmark was run for this rollout.

## Remaining decisions and next step

- Merge needs owner approval, passing final-head checks and resolved/accepted review findings. Code-owner review is required only for merges to `master` (owner decision recorded in the pilot closeouts).
- After merge: a docs-only `[skip ci]` closeout in `PROJECT_PROGRESS.md` on `sow/2026-Q4` with the merge SHA, then guarded cleanup of `docs/agent-instructions-rollout-q4` per `AGENTS.md`. Stale bootstrap wording already in `PROJECT_PROGRESS.md` is not edited by this PR; labelling it is a closeout matter that needs explicit owner authorization.
- Machine-local: the 29-repository rollout inventory, worktrees and cross-repository status table are in the
  local parent `PROJECT_PROGRESS.md` checkpoint (not Git-tracked). The committed cross-repository table is in
  SAM-BIM/SAM's `documentation/Agent-Continuity-Rollout-Q4.md` once that PR lists all rollout PRs.