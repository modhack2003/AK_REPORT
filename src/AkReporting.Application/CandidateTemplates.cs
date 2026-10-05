using AkReporting.Contracts;

namespace AkReporting.Application;

// Field discovery scaffolding only. No units, intervals, interpretations or credentials.
public static class CandidateTemplates
{
    public static IReadOnlyList<TemplateDefinition> All() =>
    [
        Template("CBC", "Complete Blood Count", 1,
            Section("counts", "Cell measurements", SectionLayout.ResultTable,
                Numeric("hemoglobin", "Hemoglobin"), Numeric("rbc", "RBC count"), Numeric("wbc", "WBC count"),
                Numeric("hematocrit", "Hematocrit"), Numeric("mcv", "MCV"), Numeric("mch", "MCH"),
                Numeric("mchc", "MCHC"), Numeric("platelets", "Platelet count")),
            Section("differential", "Differential measurements (candidate)", SectionLayout.ResultTable,
                Numeric("neutrophils", "Neutrophils"), Numeric("lymphocytes", "Lymphocytes"),
                Numeric("monocytes", "Monocytes"), Numeric("eosinophils", "Eosinophils"), Numeric("basophils", "Basophils")),
            Comments()),
        Template("LFT", "Liver Function Tests", 2,
            Section("panel", "Measurements", SectionLayout.ResultTable,
                Numeric("bilirubin_total", "Total bilirubin"), Numeric("bilirubin_direct", "Direct bilirubin"),
                Numeric("alt", "ALT"), Numeric("ast", "AST"), Numeric("alp", "ALP"),
                Numeric("albumin", "Albumin"), Numeric("protein_total", "Total protein")), Comments()),
        Template("URINE", "Urine Routine", 3,
            Section("physical", "Physical examination", SectionLayout.ResultTable, Text("color", "Color"), Text("appearance", "Appearance")),
            Section("chemical", "Chemical examination", SectionLayout.ResultTable,
                Numeric("ph", "pH"), Numeric("specific_gravity", "Specific gravity"), Text("protein", "Protein"), Text("glucose", "Glucose")),
            Section("microscopy", "Microscopy", SectionLayout.StructuredGroup,
                Text("cells", "Cells / observations", true), Text("casts", "Casts", true), Text("crystals", "Crystals", true), Text("other", "Other observations", true)), Comments()),
        Template("HISTO", "Histopathology", 4,
            Section("clinical", "Clinical details", SectionLayout.Narrative, Text("details", "Clinical details", true)),
            Section("specimen", "Specimen", SectionLayout.Narrative, Text("description", "Specimen / site", true)),
            Section("gross", "Gross description", SectionLayout.Narrative, Text("description", "Gross description", true)),
            Section("microscopic", "Microscopic description", SectionLayout.Narrative, Text("description", "Microscopic description", true)),
            Section("diagnosis", "Diagnosis", SectionLayout.Narrative, Text("diagnosis", "Diagnosis", true)), Comments()),
        Template("ECG", "Electrocardiogram", 5,
            Section("measurements", "Manually transcribed measurements (candidate)", SectionLayout.ResultTable,
                Numeric("rate", "Rate"), Numeric("pr", "PR interval"), Numeric("qrs", "QRS duration"),
                Numeric("qt", "QT interval"), Numeric("qtc", "QTc (externally supplied)"), Numeric("axis", "QRS axis")),
            Section("rhythm", "Rhythm", SectionLayout.StructuredGroup, Text("description", "Rhythm", true)),
            Section("interpretation", "Interpretation", SectionLayout.Narrative, Text("description", "Clinician-authored interpretation", true)), Comments())
    ];
    private static TemplateDefinition Template(string code, string title, int id, params SectionDefinition[] sections) => new()
    {
        VersionId = Guid.Parse($"a0000000-0000-0000-0000-{id:D12}"), ReportTypeCode = code,
        Title = title, Version = 1, ReviewStatus = ReviewStatus.Draft, Sections = sections.ToList()
    };
    private static SectionDefinition Section(string code, string title, SectionLayout layout, params FieldDefinition[] fields) =>
        new() { Code = code, Title = title, Layout = layout, Fields = fields.ToList() };
    private static FieldDefinition Numeric(string code, string label) => new() { Code = code, Label = label, Kind = ResultKind.Numeric };
    private static FieldDefinition Text(string code, string label, bool multi = false) =>
        new() { Code = code, Label = label, Kind = multi ? ResultKind.Multiline : ResultKind.Text, MaxLength = multi ? 30000 : 2000 };
    private static SectionDefinition Comments() => new()
    {
        Code = "comments", Title = "Comments", Layout = SectionLayout.Narrative, Optional = true,
        Fields = [Text("comment", "Comment", true)]
    };
}
