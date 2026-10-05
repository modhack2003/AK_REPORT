# Third-party components

Review licenses and exact dependency versions as part of release packaging. This file does not assign a license to the diagnostic-center application itself.

| Component | Use | License/source |
|---|---|---|
| Noto Sans / Noto Serif | Embedded report/desktop fonts | SIL Open Font License 1.1; complete notice at `assets/fonts/LICENSE` |
| PDFsharp 6.2.0 | PDF output/measurement | MIT; https://github.com/empira/PDFsharp |
| Open XML SDK 3.3.0 | DOCX package generation/validation | MIT; https://github.com/dotnet/Open-XML-SDK |
| Npgsql 10.0.0 | PostgreSQL access | PostgreSQL license; https://github.com/npgsql/npgsql |
| Newtonsoft.Json 13.0.4 | net48 HTTP contract serialization | MIT; https://github.com/JamesNK/Newtonsoft.Json |
| PdfPig 0.1.11 | Independent test-only PDF parsing | Apache-2.0; https://github.com/UglyToad/PdfPig |
| xUnit / test tooling | Test-only verification | See the packages' included licenses; https://github.com/xunit/xunit |

The release dependency inventory must also include transitive packages and the Microsoft runtime/reference assemblies under their applicable distribution terms. Keep applicable notices with installers and offline redistribution media.
