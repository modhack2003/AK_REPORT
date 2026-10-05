# Template and document engine

Developer-maintained schema/layout JSON is versioned and hashed. No executable expressions, HTML, scripts, SQL or arbitrary file references are accepted. Conditions use explicit section visibility settings; future predicates must use a bounded declarative AST, never code evaluation.

* A4 portrait, coordinates in points (72 pt/inch), margins configured in millimeters.
* Top/bottom margins reserve physical letterhead; center branding is not added over preprinted areas.
* Repeated patient/report identifier and page X/Y footer fit inside printable bounds.
* Numeric/result-table and narrative/group presentation are schema-level choices.
* A single measured page plan supplies PDF and desktop preview/printing. Text wraps by font measurement; long tokens split by Unicode text elements; repeated tables continue with headers. Signature/stamp attribution blocks stay together and move to a new page if needed.
* PDF uses embedded, licensed fixed fonts. Font identity, template definition, immutable structured revision and engine version are retained. A supported glyph must exist; unsupported writing systems cannot silently become missing-character boxes.
* DOCX uses WordprocessingML generated from the same page plan, fixed A4 section sizes, pinned margins and explicit page breaks. Word/printer reflow must be checked on the center's Office/LibreOffice versions; PDF remains the print authority.
* Controlled formatting: supported font family, bounded point size, bold/italic/underline/alignment and optional empty-section visibility. Header identifiers, labels, result column structure and attribution cannot be independently moved or removed.
* Exported DOCX edits do not feed back into the database; correction uses the structured editor.

Golden tests examine bytes, extracted PDF text, page count, bounds, signature position, page breaks and document XML. Hardware acceptance measures top/bottom offsets against the real pad and checks printer imageable areas at 100% scale.

A layout/engine change never rewrites existing document bytes. Old document bytes remain the exact historical artifact; regenerating old revisions requires the corresponding archived renderer/font build, not merely the latest application binary.
