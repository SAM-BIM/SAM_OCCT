# Project Instructions

This repository may be worked on by different AI agents,
models, sessions, accounts, and computers.

Git and committed repository files are the authoritative project state.
Do not rely on previous conversation memory.

## Branch context (2026-Q4)

- Active development branch: `sow/2026-Q4` (created 2026-10-06 from `master` `25801568`).
- `sow/2026-Q3` is frozen. It is the preserved record of the final Q3 release (SAM_Deploy `v20261006.1`,
  deploy SHA `1bee804f`). Do not commit to, rebase, rename, archive or delete it.
- `master` is the product line. It does not carry `AGENTS.md` or `PROJECT_PROGRESS.md`; these internal files live on
  `sow/*` branches only. Never merge or restore them onto `master`.

## Before starting work

Before making significant changes:

1. Read this `AGENTS.md`.
2. Inspect the headings in `PROJECT_PROGRESS.md`, then read the latest
   dated Q4 closeout and the sections relevant to this task. Read the
   record document and live description of any PR you are continuing.
3. Understand the relevant existing code, architecture, tests, and conventions.
4. Review the current Git branch and working tree.
5. Continue from the documented current state.

Prefer small, safe, targeted changes consistent with existing conventions.
Avoid unrelated refactoring unless it is required for the task.

## Repository operations and context

- Validate fetch and push URLs before network operations; target only
  `github.com/SAM-BIM/<repository>`. Use explicit SAM-BIM repository/API
  targets; avoid automatic fork-parent discovery. Do not contact HoareLea
  repositories or request their access/SAML authorization.
- Preserve historical attribution, legacy records and external integration
  references; changes to integrations require a separate migration decision.
- Treat dated bootstrap/Q2/Q3 entries as historical evidence. Check Git and
  live PR metadata before acting on old status/next-step text. Read earlier
  evidence when the task depends on it; do not load whole histories by default.
- These instructions and `CLAUDE.md` belong on `sow/*` and their feature branches;
  never promote internal agent files to `master`.

## Quarter transition (owner-approved release work)

- Promote approved Q4 product changes through a reviewed release PR. A normal
  merge includes internal files: prepare and verify the promotion tree excludes
  `AGENTS.md`, `PROJECT_PROGRESS.md`, `CLAUDE.md` and this internal pilot record
  (`documentation/Agent-Continuity-Pilot-Q4.md`). Classify other documentation
  separately; retain product documentation selected for release.
- Complete known-SHA Q4 closeouts before freezing `sow/2026-Q4`. Preserve the
  frozen branch as the quarter's record: no commits, rebase, rename, archive or deletion.
- Create `sow/2027-Q1` from the owner-approved promoted product baseline. Carry
  forward useful shared instructions and the Claude import onto Q1 only; update
  the active/frozen quarter references and carry unresolved work with evidence.
- Initialize a concise Q1 progress record only as an explicitly authorized
  quarter-bootstrap operation, referencing the frozen Q4 commit for history.
  This does not widen the ordinary post-merge-only progress-closeout exception.
  These steps describe future release work; merging this pilot performs none of it.

## Project continuity

`PROJECT_PROGRESS.md` is updated only after a PR has merged.
During implementation, keep status/evidence on the PR branch in the
PR record document and PR description. After merge, fetch the base
branch and add the closeout entry, including the merge SHA, as a
direct docs-only commit on the base branch. Do not modify unrelated
stream entries.

The order of work is:

code + tests + evidence → final PR CI → merge →
`PROJECT_PROGRESS.md` closeout commit on the base branch.

Never change `PROJECT_PROGRESS.md` on a PR branch. Use `[skip ci]` for
the post-merge docs-only closeout after the merge SHA is known.

The closeout is written only after the merge SHA is known, so it cannot be
part of the reviewed PR; putting it in the PR would force another review/CI
cycle merely for a progress-file update. Direct commits to `sow/2026-Q4`
are therefore allowed for this one case: the post-merge, docs-only
`PROJECT_PROGRESS.md` closeout. This exception does not cover code, tests,
workflows or any other file. All code changes still go through a feature
branch and a PR.

Do not record trivial actions such as opening files,
searching the repository, or reading documentation.

Keep the PR record document and `PROJECT_PROGRESS.md` concise and
sufficient for another AI agent on another computer to continue
without access to the current conversation.

While a PR is open, its record document must contain, and after merge
the `PROJECT_PROGRESS.md` closeout entry must contain:

- current status;
- work completed;
- important decisions and assumptions;
- files changed;
- tests, builds, checks, or validation performed and results;
- unresolved issues, risks, or blockers;
- exact recommended next step.

Identify the PR, base branch and tested head SHA. Distinguish local results
from GitHub checks, including failures/skips and required dependency order.
Give reproducible commands and committed evidence paths; label machine-local
evidence and prerequisites that must be copied or provisioned elsewhere.

When updating `PROJECT_PROGRESS.md`, preserve still-relevant information
from previous sessions. Remove or replace information only when it is
obsolete, resolved, or superseded.

Before ending meaningful work, verify that the PR record document and
PR description (or, after merge, the `PROJECT_PROGRESS.md` closeout
entry) accurately represent the current repository state.

When appropriate, remind the user to commit and push changes before
switching computers, accounts, sessions, or AI agents.

Never assume conversation history, terminal history, generated files,
or uncommitted local changes will exist on another computer.
