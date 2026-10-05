using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using AkReporting.Contracts;
using AkReporting.Domain;
using AkReporting.Rendering;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;

namespace AkReporting.Tests;

public sealed class RenderingTests
{
    private sealed record GoldenSignature(int Pages, string Pdf, string Docx);
    [Theory]
    [InlineData("CBC", "CBC-001", 1)]
    [InlineData("CBC", "CBC-002", 1)]
    [InlineData("LFT", "LFT-001", 1)]
    [InlineData("URINE", "URINE-001", 2)]
    [InlineData("HISTO", "HISTO-001", 2)]
    [InlineData("ECG", "ECG-001", 1)]
    public void GoldenFixturesAreDeterministicParseableAndWithinLetterheadBounds(string code, string name, int expectedPages)
    {
        var t = Fixtures.Template(code); var revision = Fixtures.Revision(t, name); var renderer = new ReportRenderer();
        if (name == "CBC-002") revision.Data.Metadata.Patient.Name += " — a long name with µ and α for glyph coverage";
        if (name == "HISTO-001") revision.Data.Results[2].TextValue = string.Concat(Enumerable.Repeat("Synthetic narrative content for pagination review. ", 60));
        var plan = renderer.Plan(revision, t, Fixtures.Attribution());
        Assert.Equal(expectedPages, plan.Pages.Count);
        var pdf = renderer.Render(plan, "pdf", revision.CreatedAt).Bytes;
        var repeatedPdf = renderer.Render(plan, "pdf", revision.CreatedAt).Bytes;
        var difference = Enumerable.Range(0, Math.Min(pdf.Length, repeatedPdf.Length)).FirstOrDefault(i => pdf[i] != repeatedPdf[i], -1);
        Assert.True(difference < 0 && pdf.Length == repeatedPdf.Length, difference < 0 ? "PDF lengths differ" :
            Encoding.Latin1.GetString(pdf, Math.Max(0, difference - 70), Math.Min(200, pdf.Length - Math.Max(0, difference - 70))));
        using var parsed = PdfDocument.Open(pdf);
        Assert.Equal(plan.Pages.Count, parsed.NumberOfPages);
        var text = string.Join(" ", parsed.GetPages().Select(p => p.Text));
        Assert.Contains(name, text); Assert.Contains("DRAFT", text); Assert.Contains("QA-2026-000001", text);
        if (name == "CBC-002") { Assert.Contains("µ", text); Assert.Contains("α", text); }
        for (var pageIndex = 0; pageIndex < plan.Pages.Count; pageIndex++)
        {
            var actual = parsed.GetPage(pageIndex + 1);
            Assert.All(actual.Letters, letter =>
            {
                Assert.True(letter.GlyphRectangle.Left >= plan.Left - 1);
                Assert.True(letter.GlyphRectangle.Right <= plan.Width - plan.Right + 1);
                Assert.True(letter.GlyphRectangle.Top <= plan.Height - plan.Top + 1);
                Assert.True(letter.GlyphRectangle.Bottom >= plan.Bottom - 1);
            });
            var actualImages = actual.GetImages().ToArray();
            Assert.Equal(plan.Pages[pageIndex].Images.Count, actualImages.Length);
            for (var imageIndex = 0; imageIndex < actualImages.Length; imageIndex++)
            {
                var expected = plan.Pages[pageIndex].Images[imageIndex]; var bounds = actualImages[imageIndex].Bounds;
                Assert.InRange(Math.Abs(bounds.Left - expected.X), 0, .05);
                Assert.InRange(Math.Abs(bounds.Top - (plan.Height - expected.Y)), 0, .05);
                Assert.InRange(Math.Abs(bounds.Width - expected.Width), 0, .05);
                Assert.InRange(Math.Abs(bounds.Height - expected.Height), 0, .05);
            }
        }
        var docx = renderer.Render(plan, "docx", revision.CreatedAt).Bytes;
        Assert.Equal(docx, renderer.Render(plan, "docx", revision.CreatedAt).Bytes);
        var manifest = JsonSerializer.Deserialize<Dictionary<string, GoldenSignature>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-manifest.json")))!;
        Assert.Equal(manifest[name].Pages, parsed.NumberOfPages);
        Assert.Equal(manifest[name].Pdf, Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant());
        Assert.Equal(manifest[name].Docx, Convert.ToHexString(SHA256.HashData(docx)).ToLowerInvariant());
        using var document = WordprocessingDocument.Open(new MemoryStream(docx), false);
        var errors = new OpenXmlValidator().Validate(document).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.Description)));
        Assert.Equal(plan.Pages.Count - 1, document.MainDocumentPart!.Document.Descendants<DocumentFormat.OpenXml.Wordprocessing.Break>().Count());
        foreach (var page in plan.Pages)
        {
            Assert.All(page.Text, block =>
            {
                Assert.InRange(block.X, plan.Left - .01, plan.Width - plan.Right);
                Assert.InRange(block.Y, plan.Top - .01, plan.Height - plan.Bottom);
                Assert.True(block.X + block.Width <= plan.Width - plan.Right + .01);
                Assert.True(block.Y + block.Height <= plan.Height - plan.Bottom + .01);
            });
            Assert.All(page.Images, image => Assert.True(image.Y >= plan.Top && image.Y + image.Height <= plan.Height - plan.Bottom));
        }
        var images = plan.Pages.SelectMany(p => p.Images).ToArray();
        Assert.Equal(2, images.Length); Assert.Contains(images, i => i.Role == "signature"); Assert.Contains(images, i => i.Role == "stamp");
        var artifactRoot = Environment.GetEnvironmentVariable("AK_GOLDEN_OUTPUT");
        if (artifactRoot != null)
        {
            if (!Path.IsPathRooted(artifactRoot))
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (!File.Exists(Path.Combine(directory.FullName, "AkReporting.slnx")))
                    directory = directory.Parent ?? throw new InvalidOperationException("Cannot locate workspace for synthetic golden output.");
                artifactRoot = Path.Combine(directory.FullName, artifactRoot);
            }
            Directory.CreateDirectory(artifactRoot);
            File.WriteAllBytes(Path.Combine(artifactRoot, name + ".pdf"), pdf);
            File.WriteAllBytes(Path.Combine(artifactRoot, name + ".docx"), docx);
            File.WriteAllText(Path.Combine(artifactRoot, name + ".plan.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    [Fact]
    public void LongNarrativeAndUnbrokenTokensSplitWithoutDroppingContent()
    {
        var t = Fixtures.Template("HISTO"); var r = Fixtures.Revision(t, "LONG-HISTO");
        var marker = "END-SYNTHETIC-NOTE";
        r.Data.Results[2].TextValue = string.Join(" ", Enumerable.Repeat("Synthetic long text, not a clinical statement.", 250)) + " " + new string('Z', 1200) + marker;
        var renderer = new ReportRenderer(); var plan = renderer.Plan(r, t, Fixtures.Attribution());
        Assert.True(plan.Pages.Count > 3);
        using var pdf = PdfDocument.Open(renderer.Render(plan, "pdf", r.CreatedAt).Bytes);
        var text = string.Join("", pdf.GetPages().Select(p => p.Text));
        Assert.Contains(marker, text);
        Assert.Equal(1200, text.Count(c => c == 'Z'));
    }
    [Fact]
    public void LargeRepeatedTablesRepeatColumnHeadersOnContinuationPages()
    {
        var t = Fixtures.Template("CBC"); t.Sections.RemoveRange(1, 2); t.Sections[0].Repeatable = true;
        var r = Fixtures.Revision(t, "TABLE-QA"); var original = r.Data.Results.ToArray();
        for (var row = 1; row < 30; row++)
        foreach (var cell in original) r.Data.Results.Add(new ReportResult { SectionCode = cell.SectionCode, FieldCode = cell.FieldCode, Row = row, Kind = cell.Kind, NumericValue = cell.NumericValue });
        var plan = new ReportRenderer().Plan(r, t, null);
        Assert.True(plan.Pages.Count > 3);
        Assert.All(plan.Pages, p => Assert.Equal(4, p.Text.Count(x => x.Role == "table-header")));
    }
    [Fact]
    public void MissingGlyphAndInvalidAssetFailWithoutProducingMisleadingOutput()
    {
        var t = Fixtures.Template("ECG"); var r = Fixtures.Revision(t); r.Data.Metadata.Patient.Name = "Unsupported Bengali script পরীক্ষা";
        Assert.Throws<ValidationException>(() => new ReportRenderer().Plan(r, t, null));
        Assert.Throws<ValidationException>(() => new ReportRenderer().ValidateImage(new byte[40]));
    }
    [Fact]
    public async Task ConcurrentRenderingDoesNotCrossContaminateDocumentsOrChangeTheirBytes()
    {
        var t = Fixtures.Template("CBC"); var r = Fixtures.Revision(t); var renderer = new ReportRenderer();
        var plan = renderer.Plan(r, t, Fixtures.Attribution()); var expected = renderer.Render(plan, "pdf", r.CreatedAt).Bytes;
        var documents = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => renderer.Render(plan, "pdf", r.CreatedAt).Bytes)));
        Assert.All(documents, bytes => Assert.Equal(expected, bytes));
    }
    [Fact]
    public void ControlledSerifFormattingUsesTheSameEngineAndEmbeddedGlyphs()
    {
        var t = Fixtures.Template("ECG"); var r = Fixtures.Revision(t); r.Data.Formatting.FontFamily = "Noto Serif";
        r.Data.Formatting.Bold = true; r.Data.Formatting.Italic = true; r.Data.Formatting.Underline = true; r.Data.Formatting.Alignment = TextAlignment.Right;
        var renderer = new ReportRenderer(); var plan = renderer.Plan(r, t, Fixtures.Attribution());
        Assert.Equal("Noto Serif", plan.FontFamily);
        var bytes = renderer.Render(plan, "pdf", r.CreatedAt).Bytes;
        Assert.Equal(bytes, renderer.Render(plan, "pdf", r.CreatedAt).Bytes);
        using var pdf = PdfDocument.Open(bytes); Assert.Contains("QA-2026-000001", string.Join(" ", pdf.GetPages().Select(p => p.Text)));
        using var docx = WordprocessingDocument.Open(new MemoryStream(renderer.Render(plan, "docx", r.CreatedAt).Bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(docx));
    }
}
