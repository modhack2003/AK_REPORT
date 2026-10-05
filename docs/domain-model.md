# Domain model

* `PatientMetadata`: minimal reported identity/age/sex values, scoped to case creation and immutable revision headers.
* `ReportCase`: lightweight grouping of independent investigations.
* `DiagnosticReport`: unique public ID, type, current revision pointer and retention state.
* `TemplateDefinition`: exact version ID, report type, section/field schemas, bounded layout and review evidence.
* `SectionDefinition`: table/narrative/group presentation, repeatability, required/optional visibility rules and page-break policy.
* `ReportResult`: a typed atomic value at section/field/row coordinates with pinned unit/reference text. Structured groups/tables are rows/cells; narrative fields are separately named values.
* `DoctorVersion`: immutable professional attribution and authorized signature/stamp assets; mutable doctor active status is separate from the historical version.
* `ReportRevision`: complete structured draft plus header/formatting/version pins, previous revision, state, reason, changed paths and actor/time.
* `PagePlan`: measured physical A4 text/image coordinates; shared by PDF, DOCX and WPF preview/print adapters.
* `GeneratedDocument`: immutable protected bytes, SHA-256, engine identity, revision and page count.
* `Actor` / `UserSession`: authenticated user identity/permission and revocable, hashed opaque sessions.

Domain validation is structural and technical, not diagnostic. Clinical configuration requires provenance and human review. Missing data is not inferred. Historical state cannot be silently changed.
