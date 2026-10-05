# Report engine specification

## Five representative families (provisional)

| Family | Purpose/shape | Candidate fields, pending center review |
|---|---|---|
| CBC | Blood-cell measurements, numeric result table | Hemoglobin, RBC, WBC, hematocrit, MCV, MCH, MCHC, platelets; separate differential section; comments |
| LFT | Liver-related laboratory panel, numeric table | Bilirubin fractions, ALT, AST, ALP, albumin, total protein; comments. Actual panel composition is center-specific. |
| Urine Routine | Physical/chemical/microscopy sections | Appearance/color, pH/specific gravity, protein/glucose and separate microscopy observations; specimen/collection details |
| Histopathology | Separate narrative sections; potentially many pages | Clinical details, specimen/site, gross, microscopic description, diagnosis, comment. Generic narrative schema does not replace site-specific cancer protocols. |
| ECG | Manually transcribed measurements and interpretation | Rate, PR, QRS, QT/QTc, axes, rhythm, interpretation, acquisition context. No waveform analysis or auto-diagnosis. |

These candidates are software scaffolding, not final clinical requirements. No units, ranges, formulas or interpretation defaults are supplied. NIH/NLM summaries are adequate for initial field discovery, not clinical panel approval. WHO/NABL/ICMR and professional guidelines must be reconciled with actual center methods and reports by the medical reviewer.

## Data

Each field has a stable code, label, result kind, required flag, maximum length, optional approved unit/reference text, choices and optional repeatable group. Values support numeric (including explicitly typed comparator where configured), text, qualitative, positive/negative, coded and multiline. Tables and structured groups use cells with group/row coordinates. Absence is absence, never automatically "negative" or "normal".

Schema validation rejects unknown/duplicate fields, wrong kinds, ambiguous numbers, forbidden choices, unapproved unit/range replacement, hidden required/data-bearing sections and excessive lengths. No clinical range checking or clinical flags are inferred.

Metadata records reported age and age unit without deriving age from an assumed DOB; sex is explicitly entered. Referring doctor is literal center-supplied text. Collection/report instants are explicit. Technician attribution is separate from the logged-in report writer; the writer is recorded in audit only. Medical authorization wording must match the actual process.

## Revision and issue

Initial save is revision 1. Corrections require a nonblank reason and expected current revision; each contains the full structured current state plus changed paths and previous revision. An issued report correction produces a new draft revision; it must be explicitly issued again. Historical issued revisions remain accessible and reproducible. Issue appends a revision rather than mutating a past draft.

One case produces one or more independent reports. Rendering/printing is per report revision. Clinical configuration approval and report issuance are different actions.
