# Instructions for every agent working on AK_REPORT

Read `README.md`, `docs/architecture.md`, `docs/medical-review.md`, `docs/windows-compatibility.md` and `docs/collaboration.md` before modifying this project.

## Clinical and production constraints

* Never invent or seed clinical reference ranges, units, critical values, diagnostic rules, interpretations or professional credentials. Candidate schemas remain draft until qualified center review is recorded.
* Never commit patient-identifying samples, real reports, signatures belonging to actual doctors without explicit authorization, database dumps, passwords, certificates/private keys or tokens.
* Do not label the application production-ready without the release evidence in `docs/testing.md`.
* Prove CBC/LFT/Urine Routine/Histopathology/ECG before adding other families. No LIS, billing, inventory, FHIR/sync/cloud or visual Word-like designer in v1.
* Preserve complete structured revisions and pinned template/signature versions. Historical data must not be silently mutated.

## Architecture and compatibility

* WPF client target: .NET Framework 4.8 compatibility spike; Windows 7 SP1 is an explicit client requirement. Never upgrade it to modern .NET without an agreed compatibility decision.
* .NET 10 application host and PostgreSQL are separate from the client. Do not put SQL/database credentials in WPF.
* Domain -> contracts only; application -> domain; infrastructure/rendering implement application boundaries; API composes; desktop -> contracts and HTTP only.
* All layouts come from the reusable measured report engine. No duplicated report-specific rendering or giant report HTML/text blobs.

## Multi-device collaboration

* Fetch the latest remote before starting. Use a uniquely named branch, e.g. `agent/<name>/<task>`. Do not independently rewrite shared contracts/schema/build files while another task owns them.
* Check `docs/collaboration.md` and agree ownership via the shared issue/task board. Git does not lock files. Local uncommitted work is invisible on another device.
* Keep commits small, tested and task-specific. Push the branch/checkpoint when explicitly authorized; this user has requested pushing completed work to this project's existing remote.
* Never force-push, overwrite another agent's branch, commit secrets, bypass checks or alter Git identity/configuration. Merge shared-main changes via reviewed pull requests once the baseline exists.
* A task changing migrations or contracts must describe compatibility, tests and other affected modules. Append numbered migrations; never edit an applied migration to repair a deployed database.
* Distinguish executed checks from pending Windows/printer/medical checks in handoff notes.

## Verification

Use the commands in `docs/developer-guide.md` when present. Run relevant domain/rendering tests and real-PostgreSQL integration tests for persistence changes. WPF must also compile on Windows; Linux verification does not prove Windows/printer compatibility.
