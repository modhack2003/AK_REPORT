# QA strategy and release gates

## Automated

* Domain: explicit kinds, unknown/duplicate fields, table coordinates, optional visibility, required fields, metadata lengths, clinical-config gate, unsupported Unicode and controlled formatting.
* Database: fresh migration, least-privilege runtime role, atomic IDs, case grouping, client-operation idempotency, concurrent revision conflicts, immutable historical snapshots, template/doctor changes, issue/correction, rollback, session idle expiry, lockout, hash verification, document access.
* Rendering: synthetic CBC-001, CBC-002, LFT-001, URINE-001, HISTO-001 and ECG-001. Byte-repeatability, real PDF parser extraction/page count, page-plan geometry, long text/tokens, repeated table headers, pinned margins, signature/stamp placement, DOCX OpenXML validation and explicit page breaks.
* ECHO-001/TMT-001: deliberately deferred until representative engine and medical schemas are approved; never create fake clinical fixtures merely to satisfy a name list.
* API: authentication/roles, stale updates, errors, payload bounds, document authorization, no leakage to unauthenticated/receptionist requests.

Golden values are invented **software-only synthetic test input**, not validated medical reports or reference examples. No inference of normal/abnormal is permitted.

## Hardware/manual acceptance (not replaceable by Linux tests)

Win7 SP1 x86/x64, actual Win10 and Win11 builds; inkjet and laser models; 100% A4 output; pad measurements; long names/notes; all used characters/scripts; signature/stamp images; large/multiple pages; blank optional fields; repeated print; printer offline/paperout/cancel; SQL restart; unexpected application/host/power shutdown; disk-full; failed PDF render; template/doctor changes; two writers; trusted TLS on Win7; stale/offline client recovery.

Record actual versions, evidence and defects in a release acceptance record. A CI compile cannot certify Win7 or printer compatibility. Physical printing acknowledgement must not be inferred from spool success.

## Production gate

Release requires passing automated checks, center sample comparison, documented qualified clinical approval, reproducible historical documents, database restore drill, least-privilege installation, runtime/OS/printer evidence, crash/restart testing, access/retention review, signed installer/dependency inventory and rollback procedure. Until then release status is **engineering validation in progress**.
