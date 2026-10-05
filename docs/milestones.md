# Development milestones

| Phase | Deliverable | Exit evidence |
|---|---|---|
| 0 | Research/specification and compatibility spike | Center samples, review register, runtime/OS/printer evidence |
| 1 | Architecture/PostgreSQL | Fresh migration, restricted roles, ERD/integrity tests |
| 2 | Core WPF/API | Login, case grouping, manual structured entry, server error handling |
| 3 | Report engine | Typed values, separate documents, schema validation |
| 4 | Versioned templates | Immutable versions, evidence-backed clinical approval |
| 5 | PDF/DOCX/print | Deterministic artifacts, actual preview/driver acceptance |
| 6 | Doctor/signature library | Center-verified profiles, bounded assets, immutable versions |
| 7 | Revisions | Concurrent correction, history, issue/reissue, crash rollback |
| 8 | CBC/LFT/urine/histo/ECG | Center samples and qualified clinical + physical acceptance |
| 9 | Remaining families | Family-specific research and approvals before implementation |
| 10 | Hardening | Recovery, security, performance, multiuser/hardware checks |
| 11 | Deployment/maintenance | Signed installer, restore drill, rollback, operator training |

Initial code spans the vertical foundations needed to exercise phases 1–8. Completing code is not an exit from phases 0, 8, 10 or 11. Track unresolved evidence explicitly in release notes.
