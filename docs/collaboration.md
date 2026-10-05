# Multi-agent, multi-device collaboration

Remote: **https://github.com/modhack2003/AK_REPORT**

## Start a task

```sh
git clone https://github.com/modhack2003/AK_REPORT.git
git fetch origin
git switch -c agent/<agent-name>/<task-name> origin/main
```

For an existing clone, inspect status first and preserve any local work. Do not pull into a dirty worktree or blindly resolve another agent's changes.

Create/claim a GitHub issue or shared-board task **before** working. State the base commit, owned paths, deliverable, dependencies and verification. Claims require coordination; this Markdown is a handoff reference, not a distributed locking mechanism.

## Work lanes

| Lane | Paths | Coordination |
|---|---|---|
| Architecture/contracts | `docs/architecture.md`, `src/AkReporting.Contracts/`, solution/build files | One owner; all module agents review contract changes |
| Domain/application | `src/AkReporting.Domain/`, `src/AkReporting.Application/` | Coordinate persistence/rendering interface changes |
| Database/API/security | `database/`, `src/AkReporting.Infrastructure/`, `src/AkReporting.Api/` | One migration owner; no edits to deployed migrations |
| Document engine | `src/AkReporting.Rendering/`, font assets | Coordinate shared page-plan changes with desktop |
| Desktop | `src/AkReporting.Desktop/` | Windows build/OS/printer evidence required |
| Clinical research | `docs/medical-review.md`, reviewed template definitions | Qualified center approval required; no fabricated medical values |
| QA/deployment | `tests/`, workflows, operational guides | Add independent assertions and capture executed evidence |

The five-report foundation is integrated into `main`. Claim the next module/evidence task through the shared issue board; coordinate before changing shared contracts or persistence boundaries.

## Publish and integrate

1. Fetch `origin` and inspect incoming changes.
2. Build/test the owned task and inspect `git status`, `git diff`, recent history and staged content for secrets/PHI.
3. Commit only owned changes; record checks and unresolved gates.
4. Push your uniquely named branch with upstream tracking.
5. Open a pull request with scope, medical-data impact, migration/API compatibility and test evidence. Require review before merging into the shared baseline.
6. Update release notes/handoff status when the PR merges. Other devices fetch/pull the merged commit before starting dependent work.

Never claim a pushed branch is already merged. Never overwrite remote history. If two tasks overlap, coordinate/rebase or merge deliberately and rerun affected checks.

## Current handoff

* Architecture/specification baseline: `985635a`.
* Five-report foundation integrated into `main` at `3d2cc85` through [PR #2](https://github.com/modhack2003/AK_REPORT/pull/2).
* [Main CI](https://github.com/modhack2003/AK_REPORT/actions/runs/37276000783) passed server/PostgreSQL checks and the hosted Windows net48 build. There are 28 passing automated checks and pinned synthetic golden hashes.
* Center report samples, professional attribution records, letterhead dimensions, first-host OS and qualified clinical approval are outstanding.
* Five candidate schemas must remain draft. Runtime/printer compatibility remains provisional until actual Windows hardware checks pass.
* Follow `docs/milestones.md`; do not parallelize dozens of unreviewed report templates ahead of the engine.

Current next-work lanes:

* [#3 — v1 operator/revision/archive/recovery hardening](https://github.com/modhack2003/AK_REPORT/issues/3): split and claim modules before changing contracts/migrations.
* [#4 — Windows/physical-printer acceptance](https://github.com/modhack2003/AK_REPORT/issues/4): actual OS/driver/pad evidence; first-host OS decision.
* [#5 — center-specific medical review](https://github.com/modhack2003/AK_REPORT/issues/5): anonymized center samples, source/SOP reconciliation and actual qualified review.

To start dependent work from the integrated baseline:

```sh
git fetch origin
git switch -c agent/<your-name>/<task> origin/main
```
