using System.Globalization;
using AkReporting.Contracts;
using AkReporting.Domain;
using PdfSharp.Drawing;

namespace AkReporting.Rendering;

/// <summary>One measured layout implementation for all report families and output adapters.</summary>
public sealed class LayoutEngine
{
    public PagePlan Build(ReportRevision revision, TemplateDefinition template, DoctorVersion? doctor)
    {
        ReportValidator.Draft(revision.Data, template, revision.State == ReportState.Issued);
        using var flow = new Flow(revision, template);
        flow.NewPage();
        var metadata = revision.Data.Metadata;
        flow.Paragraph("Age / Sex: " + (metadata.Patient.Age.HasValue ? metadata.Patient.Age + " " + metadata.Patient.AgeUnit : "Not recorded") + " / " + (metadata.Patient.Sex.Length > 0 ? metadata.Patient.Sex : "Not recorded"));
        flow.Paragraph("Referring doctor: " + metadata.ReferringDoctor);
        flow.Paragraph("Reported: " + metadata.ReportTime.ToOffset(TimeSpan.FromMinutes(330)).ToString("dd MMM yyyy HH:mm 'IST'", CultureInfo.InvariantCulture));
        if (metadata.CollectionTime.HasValue) flow.Paragraph("Collected: " + metadata.CollectionTime.Value.ToOffset(TimeSpan.FromMinutes(330)).ToString("dd MMM yyyy HH:mm 'IST'", CultureInfo.InvariantCulture));
        if (metadata.Specimen.Length > 0) flow.Paragraph("Specimen: " + metadata.Specimen + (metadata.SpecimenId.Length == 0 ? "" : " | ID: " + metadata.SpecimenId));
        if (metadata.Method.Length > 0) flow.Paragraph("Method: " + metadata.Method);
        if (metadata.ClinicalHistory.Length > 0) flow.Paragraph("Clinical history: " + metadata.ClinicalHistory);
        flow.Gap(8);
        foreach (var section in template.Sections)
        {
            if (revision.Data.Formatting.HiddenSections.Contains(section.Code)) continue;
            if (section.PageBreakBefore) flow.NewPage();
            flow.Heading(section.Title);
            if (section.Layout == SectionLayout.ResultTable)
            {
                flow.TableHeader();
                var rows = section.Repeatable ? revision.Data.Results.Where(x => x.SectionCode == section.Code).Select(x => x.Row).Distinct().Order().DefaultIfEmpty(0) : new[] { 0 };
                foreach (var row in rows)
                    foreach (var field in section.Fields)
                    {
                        var value = revision.Data.Results.SingleOrDefault(x => x.SectionCode == section.Code && x.FieldCode == field.Code && x.Row == row);
                        flow.TableRow(field.Label + (section.Repeatable ? $" [row {row + 1}]" : ""), Display(value), field.Unit, field.ReferenceText);
                    }
            }
            else
            {
                var rows = section.Repeatable ? revision.Data.Results.Where(x => x.SectionCode == section.Code).Select(x => x.Row).Distinct().Order().DefaultIfEmpty(0) : new[] { 0 };
                foreach (var row in rows)
                    foreach (var field in section.Fields)
                    {
                        var value = revision.Data.Results.SingleOrDefault(x => x.SectionCode == section.Code && x.FieldCode == field.Code && x.Row == row);
                        flow.Paragraph(field.Label + (section.Repeatable ? $" [row {row + 1}]" : "") + ": " + Display(value), revision.Data.Formatting.Alignment);
                    }
            }
            flow.Gap(8);
        }
        if (metadata.TechnicianAttribution.Length > 0) flow.Paragraph("Technician: " + metadata.TechnicianAttribution);
        if (doctor != null) flow.Attribution(doctor);
        flow.Finish();
        return flow.Plan;
    }
    private static string Display(ReportResult? value) => value == null ? "Not entered" : value.Kind == ResultKind.Numeric
        ? value.Comparator + value.NumericValue!.Value.ToString(CultureInfo.InvariantCulture) : value.TextValue;

    private sealed class Flow : IDisposable
    {
        public PagePlan Plan { get; }
        private readonly ReportRevision revision;
        private readonly TemplateDefinition template;
        private ReportPage page = null!;
        private double y;
        private readonly XGraphics measuring;
        private readonly double contentWidth;
        private readonly double limit;
        private readonly double size;
        private readonly double line;
        private double BodyTop;
        private double TableTop;
        public Flow(ReportRevision r, TemplateDefinition t)
        {
            revision = r; template = t;
            Plan = new PagePlan { Top = Mm(t.Layout.TopMm), Bottom = Mm(t.Layout.BottomMm), Left = Mm(t.Layout.LeftMm), Right = Mm(t.Layout.RightMm), FontFamily = r.Data.Formatting.FontFamily };
            size = r.Data.Formatting.FontSize;
            line = size * 1.65;
            contentWidth = Plan.Width - Plan.Left - Plan.Right;
            limit = Plan.Height - Plan.Bottom - 18;
            measuring = XGraphics.CreateMeasureContext(new XSize(Plan.Width, Plan.Height), XGraphicsUnit.Point, XPageDirection.Downwards);
        }
        public void NewPage()
        {
            if (Plan.Pages.Count >= 100) throw new ValidationException("Report exceeds the 100-page software limit.");
            page = new ReportPage(); Plan.Pages.Add(page); y = Plan.Top;
            var draft = revision.State == ReportState.Draft || template.ReviewStatus == ReviewStatus.Draft;
            Header(template.Title + (draft ? " - DRAFT / NOT FOR CLINICAL USE" : ""), 11, true);
            Header(revision.PublicNumber + " | Revision " + revision.Number, 9, false);
            Header("Patient: " + revision.Data.Metadata.Patient.Name + (revision.Data.Metadata.Patient.LocalId.Length > 0 ? " | ID: " + revision.Data.Metadata.Patient.LocalId : ""), 9, true);
            y += 7;
            BodyTop = y;
            TableTop = y + 2 * line;
            if (limit - BodyTop < 120) throw new ValidationException("Patient header and letterhead leave insufficient printable area.");
        }
        private void Header(string text, double fontSize, bool bold)
        {
            foreach (var value in Wrap(text, contentWidth, fontSize, bold))
            {
                Add(value, Plan.Left, y, contentWidth, fontSize * 1.65, fontSize, bold, false, false, TextAlignment.Left, "header");
                y += fontSize * 1.65;
            }
        }
        private void Ensure(double height)
        {
            if (height > limit - BodyTop) throw new ValidationException("An unsplittable block cannot fit in the printable area.");
            if (y + height > limit) NewPage();
        }
        public void Gap(double height) { y += height; }
        public void Heading(string title)
        {
            var lines = Wrap(title, contentWidth, size, true);
            Ensure((lines.Count + 2) * line);
            foreach (var text in lines) { Add(text, Plan.Left, y, contentWidth, line, size, true, false, false, TextAlignment.Left, "section"); y += line; }
        }
        public void Paragraph(string text, TextAlignment alignment = TextAlignment.Left)
        {
            var f = revision.Data.Formatting;
            foreach (var value in Wrap(text, contentWidth, size, f.Bold, f.Italic))
            {
                Ensure(line);
                Add(value, Plan.Left, y, contentWidth, line, size, f.Bold, f.Italic, f.Underline, alignment, "body");
                y += line;
            }
        }
        private static readonly double[] Fractions = [.34, .20, .14, .32];
        public void TableHeader()
        {
            Ensure(line * 2);
            TableTop = y;
            var labels = new[] { "Investigation", "Result", "Unit", "Reference text" };
            var x = Plan.Left;
            for (var i = 0; i < 4; i++) { Add(labels[i], x, y, contentWidth * Fractions[i] - 6, line, 9, true, false, false, TextAlignment.Left, "table-header"); x += contentWidth * Fractions[i]; }
            y += line + 3;
        }
        public void TableRow(string label, string value, string unit, string reference)
        {
            var values = new[] { label, value, unit, reference };
            var formatting = revision.Data.Formatting;
            var wrapped = values.Select((v, i) => Wrap(v, contentWidth * Fractions[i] - 6, size,
                i == 1 && formatting.Bold, i == 1 && formatting.Italic)).ToArray();
            var count = wrapped.Max(x => x.Count);
            for (var index = 0; index < count; index++)
            {
                if (y + line > limit) { NewPage(); TableHeader(); }
                if (limit - TableTop < 2 * line) throw new ValidationException("Table header leaves insufficient space.");
                var x = Plan.Left;
                for (var col = 0; col < 4; col++)
                {
                    Add(index < wrapped[col].Count ? wrapped[col][index] : "", x, y, contentWidth * Fractions[col] - 6, line, size,
                        col == 1 && formatting.Bold, col == 1 && formatting.Italic, col == 1 && formatting.Underline,
                        col == 1 ? formatting.Alignment : TextAlignment.Left, "table-cell");
                    x += contentWidth * Fractions[col];
                }
                y += line;
            }
            y += 3;
        }
        public void Attribution(DoctorVersion doctor)
        {
            var texts = new[] { doctor.DisplayName, doctor.Qualification, doctor.Designation,
                doctor.RegistrationNumber.Length > 0 ? "Registration: " + doctor.RegistrationNumber : "", doctor.Specialty }.Where(x => x.Length > 0);
            var wrapped = texts.SelectMany(t => Wrap(t, contentWidth * .65, 9)).ToArray();
            var imageHeight = doctor.SignaturePng != null || doctor.StampPng != null ? 50 : 0;
            Ensure(12 + imageHeight + wrapped.Length * 14.85);
            y += 12;
            var x = Plan.Left;
            foreach (var (png, role) in new[] { (doctor.SignaturePng, "signature"), (doctor.StampPng, "stamp") })
            {
                if (png == null) continue;
                using var image = XImage.FromStream(new MemoryStream(png));
                var factor = Math.Min(120.0 / image.PixelWidth, 46.0 / image.PixelHeight);
                page.Images.Add(new PositionedImage { X = x, Y = y, Width = image.PixelWidth * factor, Height = image.PixelHeight * factor, Png = png, Role = role });
                x += 135;
            }
            y += imageHeight;
            foreach (var text in wrapped) { Add(text, Plan.Left, y, contentWidth * .65, 14.85, 9, false, false, false, TextAlignment.Left, "attribution"); y += 14.85; }
        }
        public void Finish()
        {
            for (var i = 0; i < Plan.Pages.Count; i++)
            {
                page = Plan.Pages[i];
                Add(revision.PublicNumber + " | Revision " + revision.Number + $" | Page {i + 1}/{Plan.Pages.Count}", Plan.Left, limit + 4, contentWidth, 14, 8, false, false, false, TextAlignment.Right, "footer");
            }
        }
        public void Dispose() => measuring.Dispose();
        private void Add(string text, double x, double atY, double width, double height, double fontSize, bool bold, bool italic, bool underline, TextAlignment alignment, string role)
        {
            ReportFonts.ValidateGlyphs(text, ReportFonts.Font(fontSize, bold, italic, Plan.FontFamily));
            page.Text.Add(new PositionedText { Text = text, X = x, Y = atY, Width = width, Height = height, FontSize = fontSize,
                Bold = bold, Italic = italic, Underline = underline, Alignment = alignment, Role = role });
        }
        private List<string> Wrap(string text, double width, double fontSize, bool bold = false, bool italic = false)
        {
            var font = ReportFonts.Font(fontSize, bold, italic, Plan.FontFamily);
            ReportFonts.ValidateGlyphs(text, font);
            var result = new List<string>();
            foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace('\t', ' ').Split('\n'))
            {
                var remaining = paragraph;
                if (remaining.Length == 0) { result.Add(""); continue; }
                while (remaining.Length > 0)
                {
                    if (measuring.MeasureString(remaining, font).Width <= width) { result.Add(remaining); break; }
                    var elements = StringInfo.ParseCombiningCharacters(remaining);
                    var low = 1; var high = elements.Length;
                    while (low < high)
                    {
                        var middle = (low + high + 1) / 2;
                        var length = middle == elements.Length ? remaining.Length : elements[middle];
                        if (measuring.MeasureString(remaining[..length], font).Width <= width) low = middle;
                        else high = middle - 1;
                    }
                    var end = low == elements.Length ? remaining.Length : elements[low];
                    if (measuring.MeasureString(remaining[..end], font).Width > width) throw new ValidationException("A text element cannot fit in a column.");
                    var space = remaining.LastIndexOf(' ', end - 1, end);
                    if (space > 0) end = space;
                    result.Add(remaining[..end]);
                    remaining = remaining[end..].TrimStart(' ');
                }
            }
            return result;
        }
        private static double Mm(double value) => value * 72 / 25.4;
    }
}
