# Security model

Offline does not mean unauthenticated. All business endpoints require a server-validated session and role checks.

| Role | Capabilities |
|---|---|
| Writer | Case/report creation, structured correction, preview, issue using approved templates and selected valid attribution, document export |
| Receptionist | Minimal case creation/list; no result values or signatures |
| MedicalReviewer | Clinical-configuration review/approval with evidence; writer capabilities where granted |
| Administrator | User/catalog/doctor/settings provisioning; no implicit medical qualification |

Configuration review stores human reviewer identity and evidence. The center must assign the reviewer role only to its qualified reviewer. Software permissions do not establish professional credentials.

* Passwords: salted PBKDF2-HMAC-SHA512 via ASP.NET Identity PasswordHasher, explicitly 600,000 iterations; bounded input, lockout and endpoint rate limiting. No seeded credentials.
* Sessions: cryptographically random 256-bit opaque tokens, SHA-256 hashes only in the database; idle expiry 15 minutes and absolute 8 hours; logout revokes. No credential/token logging. Desktop keeps token in memory.
* Transport: HTTPS mandatory except localhost development. Trusted local CA/certificate for LAN, TLS 1.2 Win7 compatibility. Never disable certificate verification.
* PostgreSQL: migration owner separate from limited runtime login; SCRAM, loopback-only access when colocated; no client access to port 5432. Parameterized queries throughout.
* Signatures/documents: bounded decoded PNG assets stored as immutable database bytes; authenticated render/document endpoints; no public/static signature directories. Document hashes checked before download.
* Local exports: user-selected destination, bounded fixed extensions, same-directory atomic replacement, application temp directory scoped to current user. Apply NTFS ACLs; protect host DB/service configuration with service-account ACLs. Full disk encryption where the operating system supports it is an installation measure.
* Secrets: external environment/service configuration, never committed. Bootstrap credentials read interactively from console. Migration/restore credentials remain administrative.
* Logs: structured action/entity/correlation metadata only; no request-body logging, names, result text, passwords or bearer tokens. Centralized problem responses do not expose SQL/stack traces.

Clinical controls concern medical configuration/issuance, not automatic diagnosis. Audit and append-only SQL privileges support traceability but do not constitute tamperproof external signatures.
