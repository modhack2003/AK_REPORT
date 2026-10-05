using System.Text.RegularExpressions;
using AkReporting.Contracts;

namespace AkReporting.Domain;

public static partial class ReportValidator
{
    public static void Patient(PatientMetadata p)
    {
        Required(p.Name, "Patient name", 200);
        Bounded(p.LocalId, "Patient ID", 80);
        if (p.Age is < 0 || (p.Age.HasValue && p.AgeUnit is not ("years" or "months" or "days")))
            throw new ValidationException("Enter an explicit nonnegative age and age unit.");
        if (!p.Age.HasValue && p.AgeUnit.Length != 0) throw new ValidationException("Age unit needs an age.");
        Bounded(p.Sex, "Sex", 40);
    }

    public static void Template(TemplateDefinition t)
    {
        if (t.VersionId == Guid.Empty || t.Version < 1 || !Code().IsMatch(t.ReportTypeCode))
            throw new ValidationException("Invalid template identity.");
        Required(t.Title, "Template title", 120);
        var l = t.Layout;
        if (l.FontFamily is not ("Noto Sans" or "Noto Serif") || !Finite(l.FontSize, 8, 14) ||
            !Finite(l.TopMm, 10, 100) || !Finite(l.BottomMm, 10, 70) ||
            !Finite(l.LeftMm, 10, 40) || !Finite(l.RightMm, 10, 40))
            throw new ValidationException("Template must use supported fonts and bounded A4 margins.");
        if (!Enum.IsDefined(t.ReviewStatus) || t.Sections.Count is < 1 or > 30)
            throw new ValidationException("Invalid schema status or section count.");
        if (t.ReviewStatus == ReviewStatus.Approved)
        {
            Required(t.ReviewEvidence, "Clinical review evidence", 8000);
            Required(t.ReviewedBy, "Qualified reviewer identity", 200);
            if (t.ReviewedAt == null) throw new ValidationException("Clinical approval needs a review date.");
        }
        var sections = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in t.Sections)
        {
            if (!Code().IsMatch(s.Code) || !sections.Add(s.Code) || s.Fields.Count is < 1 or > 100 ||
                !Enum.IsDefined(s.Layout)) throw new ValidationException("Invalid or duplicate section.");
            Required(s.Title, "Section title", 120);
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in s.Fields)
            {
                if (!Code().IsMatch(f.Code) || !fields.Add(f.Code) || !Enum.IsDefined(f.Kind) || f.MaxLength is < 1 or > 30000)
                    throw new ValidationException("Invalid or duplicate field definition.");
                Required(f.Label, "Field label", 120);
                Bounded(f.Unit, "Unit", 80);
                Bounded(f.ReferenceText, "Reference text", 500);
                Bounded(f.ClinicalSource, "Clinical source", 4000);
                if ((f.Unit.Length > 0 || f.ReferenceText.Length > 0) && string.IsNullOrWhiteSpace(f.ClinicalSource))
                    throw new ValidationException("Clinical units/reference text require documented provenance.");
                if (f.Choices.Count > 100 || f.Choices.Distinct(StringComparer.Ordinal).Count() != f.Choices.Count)
                    throw new ValidationException("Invalid qualitative choices.");
                foreach (var choice in f.Choices) Required(choice, "Choice", 200);
                if (f.Kind is ResultKind.Qualitative or ResultKind.PositiveNegative or ResultKind.Coded && f.Choices.Count == 0)
                    throw new ValidationException("Coded/qualitative values require explicitly reviewed choices.");
            }
        }
    }

    public static void Draft(ReportDraft d, TemplateDefinition t, bool issuing = false)
    {
        Template(t);
        if (d.TemplateVersionId != t.VersionId) throw new ValidationException("Template version mismatch.");
        Patient(d.Metadata.Patient);
        var m = d.Metadata;
        Bounded(m.ReferringDoctor, "Referring doctor", 200);
        Bounded(m.Specimen, "Specimen", 2000);
        Bounded(m.SpecimenId, "Specimen ID", 100);
        Bounded(m.Method, "Method", 2000);
        Bounded(m.ClinicalHistory, "Clinical history", 10000);
        Bounded(m.TechnicianAttribution, "Technician attribution", 500);
        if (m.ReportTime == default) throw new ValidationException("Report time is required.");
        if (m.CollectionTime > m.ReportTime) throw new ValidationException("Collection cannot be after report time.");
        var fmt = d.Formatting;
        if (fmt.FontFamily is not ("Noto Sans" or "Noto Serif") || !Finite(fmt.FontSize, 8, 14) || !Enum.IsDefined(fmt.Alignment))
            throw new ValidationException("Unsupported report formatting.");
        var schema = t.Sections.ToDictionary(x => x.Code, StringComparer.Ordinal);
        if (fmt.HiddenSections.Distinct().Count() != fmt.HiddenSections.Count)
            throw new ValidationException("Duplicate visibility settings.");
        foreach (var code in fmt.HiddenSections)
        {
            if (!schema.TryGetValue(code, out var s) || !s.Optional || s.Fields.Any(f => f.Required) || d.Results.Any(r => r.SectionCode == code))
                throw new ValidationException("Only empty optional sections can be hidden.");
        }
        if (d.Results.Count > 10000) throw new ValidationException("Too many result cells.");
        var cells = new HashSet<(string, string, int)>();
        foreach (var r in d.Results)
        {
            if (!schema.TryGetValue(r.SectionCode, out var s)) throw new ValidationException("Unknown result section.");
            var f = s.Fields.FirstOrDefault(x => x.Code == r.FieldCode) ?? throw new ValidationException("Unknown result field.");
            if (!cells.Add((r.SectionCode, r.FieldCode, r.Row)) || r.Row < 0 || r.Row > 999 || (!s.Repeatable && r.Row != 0))
                throw new ValidationException("Invalid or duplicate result coordinates.");
            if (r.Kind != f.Kind || r.Unit != f.Unit || r.ReferenceText != f.ReferenceText)
                throw new ValidationException("Result kind, unit or reference text differs from the pinned schema.");
            if (r.Kind == ResultKind.Numeric)
            {
                if (!r.NumericValue.HasValue || r.TextValue.Length != 0 ||
                    (r.Comparator.Length > 0 && (!f.AllowComparator || r.Comparator is not ("<" or "<=" or ">" or ">="))))
                    throw new ValidationException("Numeric results need an explicit numeric value and permitted comparator.");
            }
            else
            {
                if (r.NumericValue != null || r.Comparator.Length != 0) throw new ValidationException("Text results cannot carry numeric data.");
                Required(r.TextValue, f.Label, f.MaxLength);
                if (f.Choices.Count > 0 && !f.Choices.Contains(r.TextValue, StringComparer.Ordinal))
                    throw new ValidationException("Result is outside the configured vocabulary.");
            }
        }
        if (!issuing) return;
        if (t.ReviewStatus != ReviewStatus.Approved) throw new ValidationException("Draft clinical configuration cannot be issued.");
        if (d.DoctorVersionId == null) throw new ValidationException("Select the actual authorizing doctor version.");
        foreach (var s in t.Sections.Where(x => !fmt.HiddenSections.Contains(x.Code)))
        {
            var rows = s.Repeatable ? d.Results.Where(r => r.SectionCode == s.Code).Select(r => r.Row).Distinct().DefaultIfEmpty(0) : new[] { 0 };
            foreach (var row in rows)
                foreach (var f in s.Fields.Where(x => x.Required))
                    if (!cells.Contains((s.Code, f.Code, row))) throw new ValidationException($"Required field missing: {s.Code}/{f.Code} row {row}.");
        }
    }

    public static void Doctor(DoctorVersion d)
    {
        Required(d.Name, "Doctor name", 200);
        Required(d.DisplayName, "Doctor display name", 200);
        Required(d.PermissionEvidence, "Attribution/signature permission evidence", 4000);
        Bounded(d.Qualification, "Qualification", 300);
        Bounded(d.Designation, "Designation", 200);
        Bounded(d.RegistrationNumber, "Registration number", 100);
        Bounded(d.Specialty, "Specialty", 200);
        foreach (var png in new[] { d.SignaturePng, d.StampPng })
            if (png != null && (png.Length < 33 || png.Length > 2 * 1024 * 1024 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })))
                throw new ValidationException("Signature/stamp must be a bounded PNG image.");
    }

    public static void Required(string value, string label, int max)
    {
        Bounded(value, label, max);
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationException(label + " is required.");
    }
    public static void Bounded(string value, string label, int max)
    {
        if (value == null || value.Length > max || value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new ValidationException(label + " contains invalid text or exceeds its length limit.");
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i])) continue;
            if (!char.IsHighSurrogate(value[i]) || i + 1 == value.Length || !char.IsLowSurrogate(value[i + 1]))
                throw new ValidationException(label + " contains malformed Unicode.");
            i++;
        }
    }
    private static bool Finite(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Code();
}
