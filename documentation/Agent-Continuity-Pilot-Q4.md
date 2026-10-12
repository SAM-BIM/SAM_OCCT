# Q4 agent continuity pilot — 2026-10-12

Status: implemented on `docs/agent-continuity-pilot-q4`; owner review required.
Base: `sow/2026-Q4` at `e27242931e7007224c567933827f96ef137e0807`.
PR: [SAM_OCCT#74](https://github.com/SAM-BIM/SAM_OCCT/pull/74).
Companion: [SAM#191](https://github.com/SAM-BIM/SAM/pull/191) (independent; see below).
Locally validated instruction/content head: `53a19b4bbb92dd98d3b57490cd601fad298fc329`.
Superseded validated content head: `8cb1d37cdb6ca165e84ccaee1d044e0534bc4aa4`.
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

AGENTS.md also states that a release-promotion PR into `master` records its
closeout on the outgoing `sow` branch, never on `master`, and that heading
position is not a period marker. Verified at base `e272429`: no Q4 entry sits
beneath `# Historical record - 2026-Q3 (frozen)` in OCCT PROJECT_PROGRESS.md, so
no entry needs moving. The bootstrap `## Current status` text ("No Q4 product
work has started") is superseded by the SAM_OCCT#73 closeout; the owner
authorized labelling it (verbatim, nothing moved) in this pilot's post-merge closeout.

## Companion PR independence

Owner-stated: SAM#191 and SAM_OCCT#74 are independent, with no merge-order
dependency; each gets its own closeout on its own base branch. SAM#191 merged as
`02bbafb8db289ac2bd3fc3a35c54cff86e9eadbb` (SAM closeout `46e96b72`). That is
reference context only; it does not validate this PR, whose evidence is below.

## Evidence and validation

Review disposition:
- Copilot (GitHub review on `4b578b7`): asked for the final tested head in this
  record. Addressed by recording the immutable tested content SHA and commands
  here, with AGENTS.md describing the following record-only commit; the final head
  is in the PR description. Its thread is left for the reviewer to resolve.
- Kimi: review supplied externally by the owner (not posted on GitHub, so not
  verifiable from GitHub state); it also asked for reproducible commands and clear
  tested-head identity, handled as above.
- Claude review of `a0dbf98` (2026-10-12): flagged the release-closeout location,
  missing exit-code capture/Bash commands/measurement command, unlabelled
  machine-local evidence, Kimi provenance and unstated companion independence.
  Addressed in the content head above.
The master-policy contradiction was real: master already contains general
CLAUDE.md guidance (blob `14a41e63703bdf41a34bc9ab502ccc0844452dc8`). Preserve it.
Quarter promotion must use the approved master CLAUDE.md baseline, excluding the
Q4 import, rather than deleting the legacy file. Master has no AGENTS.md or
PROJECT_PROGRESS.md. No master file is changed by this PR.

Run from a checkout of this PR at its final head (PowerShell; Git and Python 3 on PATH):

```powershell
$baseRevision = 'e27242931e7007224c567933827f96ef137e0807'
$testedRevision = '53a19b4bbb92dd98d3b57490cd601fad298fc329'
$finalRevision = git rev-parse HEAD   # must equal the final head in the PR description
foreach ($revision in $testedRevision, $finalRevision) {
  git diff --check $baseRevision $revision; "check $revision exit=$LASTEXITCODE"
  git diff --name-only $baseRevision $revision
  git diff --quiet $baseRevision $revision -- PROJECT_PROGRESS.md; "progress $revision exit=$LASTEXITCODE"
}
git diff --name-only $testedRevision $finalRevision
python -c "import re,subprocess as s;t=s.run(['git','show','e27242931e7007224c567933827f96ef137e0807:PROJECT_PROGRESS.md'],capture_output=True,check=True).stdout.decode('utf-8').replace('\r\n','\n');L=t.split('\n');h=[l for l in L if re.match(r'#{1,3} ',l)];p=L[:L.index('## Branch')];print(len(t),len('\n'.join(h))+1+len('\n'.join(p)))"
```

Bash equivalent (run the same `python` line as `python3`):

```bash
base=e27242931e7007224c567933827f96ef137e0807
tested=53a19b4bbb92dd98d3b57490cd601fad298fc329
final=$(git rev-parse HEAD)   # must equal the final head in the PR description
for revision in "$tested" "$final"; do
  git diff --check "$base" "$revision"; echo "check $revision exit=$?"
  git diff --name-only "$base" "$revision"
  git diff --quiet "$base" "$revision" -- PROJECT_PROGRESS.md; echo "progress $revision exit=$?"
done
git diff --name-only "$tested" "$final"
```

Expected: every `exit=` is 0; both full-PR file lists contain only AGENTS.md,
CLAUDE.md and this record; the tested-to-final list contains only this record;
the measurement prints `16547 2317`. These are local documentation checks, not
product test or CI results.

Results (2026-10-12, Linux cloud checkout, Bash block): at `53a19b4` and at the
final head every `exit=` is 0; both full-PR lists are exactly the three files;
tested-to-final lists only this record; the measurement prints `16547 2317`.
The PowerShell block was not run on Windows in this session. Hosted checks
(`build` on windows-2022, `spdx`) are tracked by the live PR at its final head.

Machine-local evidence: the organisation audit was gathered 2026-10-12 on the
owner's workstation from its local checkouts and owner-attached copies of SAM's
continuity files (which matched that checkout). These inputs are not committed
and cannot be reproduced from this repository; another computer needs the same
set of SAM-BIM checkouts to repeat it. 43 in-scope local checkouts and live
SAM-BIM remotes audited (Solver_Upstream excluded); reviewed continuity blobs
match live Q4/default branches. OCCT is selected specifically because of its
existing standalone CLAUDE.md, not for mechanical template rollout.

Normalize CRLF to LF; read all level 1–3 progress headings plus the leading icon
closeout up to `## Branch` (the `python` line above, reproducible from the base
commit): 16,547 to 2,317 characters (86.0% less read context).
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

Next: owner reviews this pilot (the SAM companion merged independently); verify instruction loading
in fresh Claude/Codex/Cursor/Hermes sessions and measure input/cache tokens and
handoff correctness before rollout. After an approved merge only, write a known-SHA
Q4 progress closeout as docs-only `[skip ci]`. Do not merge before owner/code-owner
approval, resolution of open review findings and green final-head checks. Owner
decision (2026-10-12): owner authorization suffices for this `sow` merge;
ZiolkowskiJakub's review is required only for merges to `master`. No broad cleanup.

Generated by Michal Dengusiak & Claude Code
