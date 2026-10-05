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
| NSIS 3.11 | Windows installer compiler/runtime | zlib/libpng core; LZMA exception; full compiler `COPYING` is bundled as `docs/NSIS-License.txt`; https://nsis.sourceforge.io/License |
| PostgreSQL 17.11 EDB Windows binaries | Packaged isolated local database | PostgreSQL/server and third-party notices are retained from the archive; https://www.enterprisedb.com/download-postgresql-binaries |
| Microsoft Visual C++ Redistributable | Native runtime prerequisite | Microsoft distribution terms; original signed redistributable bundled unmodified |

The release dependency inventory must also include transitive packages and the Microsoft runtime/reference assemblies under their applicable distribution terms. Keep applicable notices with installers and offline redistribution media.
