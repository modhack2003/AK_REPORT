# Medical configuration research and review register

No anonymized center samples or expert sign-offs have been received. **All five supplied definitions are DRAFT.** No normal ranges, units, critical thresholds, diagnostic rules, interpretations, professional titles or specimen-container instructions are shipped.

## Sources accessed 2026-10-05

| ID | Authority / source | What was established | Limitations |
|---|---|---|---|
| WHO-LQMS | [WHO Laboratory quality management handbook, 2011](https://www.who.int/publications/i/item/9789241548274), ISBN 9789241548274 | Quality-management/report traceability reference based on ISO 15189/CLSI GP26-A3 | Historical handbook; not a substitute for current ISO 15189/NABL requirements |
| NLM-CBC | [NIH/NLM CBC](https://medlineplus.gov/lab-tests/complete-blood-count-cbc/) | Candidate blood-cell measurement families and blood specimen context | Patient information, not a validated method-specific reporting SOP |
| NLM-LFT | [NIH/NLM Liver function tests](https://medlineplus.gov/lab-tests/liver-function-tests/) | Candidate liver-panel analytes, blood sample, panel composition varies | Does not prescribe center menu, reference intervals or specimen processing |
| NLM-URINE | [NLM-hosted Urinalysis overview](https://medlineplus.gov/ency/article/003579.htm) | Physical, chemical and microscopy grouping | Overview only; no contents copied; specimen method and reporting units require center SOP |
| CAP-HISTO | [CAP cancer protocol index](https://www.cap.org/protocols-and-guidelines/cancer-reporting-tools/cancer-protocol-templates) | Malignant-tumor reporting requires site-specific data; protocols are versioned | Generic narrative report is insufficient for all cancer specimens. Review licensing before embedding protocol content. |
| NRCES-LAB | [NRCeS ABDM DiagnosticReportLab 6.5.0](https://nrces.in/ndhm/fhir/r4/StructureDefinition-DiagnosticReportLab.html) | Report/Observation separation, result interpreter/specimen/result concepts | Future interoperability guidance only; no v1 FHIR conformance claim |
| NABL-INDEX | [NABL public documents index](https://nabl-india.org/nabl/index.php?c=publicaccredationdoc&m=index) | Authoritative publication entry point identified | Index returned no records; current medical-lab documents/content not verified |
| ICMR | [ICMR](https://www.icmr.gov.in/) guidelines/STW endpoints attempted | Authority identified | Requests failed; applicable guidance and current editions remain unverified |
| AHA-ECG | AHA standardization/interpretation statement landing page and PubMed PMID 17322457 attempted | Target professional source identified | 403/cookie barrier; content not reviewed. ECG schema stays candidate-only pending cardiologist/center validation. |

Do not claim inaccessible documents were researched or that NABL accreditation applies to this center. Consult the current licensed ISO 15189 standard and obtain applicable NABL documents directly. Resolve ICMR applicability and cardiology standards in the next review cycle.

## Required review evidence per template version

1. Actual center test menu and anonymized specimen reports; strip identifiers before ingestion. Do not commit original reports or PHI.
2. Purpose, specimen/sample identification, method/instrument where relevant, analyte labels and result shape.
3. Units verified against actual SOP/instrument; reference interval source, method, population, age/sex applicability and authorized review. Empty intervals must be explicit, not invented.
4. Required fields and qualitative vocabulary; no silent inference from a missing value.
5. Interpretation/conclusion authored by appropriately authorized staff; no default diagnostic text.
6. Technician/doctor details, signature permission, registration and wording as actually used by the center.
7. Standards/sample conflict log: source versions, conflict, reviewer decision and dated evidence. Do not silently resolve clinical conflicts.
8. Layout/letterhead/signature acceptance recorded separately from clinical approval.

Review status: candidate -> center comparison -> medical review -> approved immutable version -> hardware/report acceptance. Software requires a reviewer/evidence record before issuing; the existence of that record is not a substitute for actual expert review.

## Remaining families

ESR/smear, KFT/lipids/glucose/HbA1c, stool, immunology, serology, microbiology/AST, cytology, molecular/PCR, ECHO and TMT are pending family-specific research and samples. Particularly do not invent antimicrobial breakpoints, PCR positivity cutoffs, QT correction, ejection-fraction calculations, exercise criteria or age-specific ranges.
