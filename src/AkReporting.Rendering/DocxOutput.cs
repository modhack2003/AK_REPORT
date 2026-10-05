using System.IO.Compression;
using AkReporting.Contracts;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace AkReporting.Rendering;

public static class DocxOutput
{
    public static byte[] Write(PagePlan plan, DateTimeOffset timestamp)
    {
        using var memory = new MemoryStream();
        using (var document = WordprocessingDocument.Create(memory, WordprocessingDocumentType.Document, true))
        {
            var core = document.AddCoreFilePropertiesPart();
            document.ChangeIdOfPart(core, "rIdCore");
            using (var emptyCore = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\"/>")))
                core.FeedData(emptyCore);
            document.PackageProperties.Creator = plan.EngineVersion;
            document.PackageProperties.Created = timestamp.UtcDateTime;
            document.PackageProperties.Modified = timestamp.UtcDateTime;
            var main = document.AddMainDocumentPart();
            document.ChangeIdOfPart(main, "rIdMain");
            main.Document = new W.Document(new W.Body());
            var body = main.Document.Body!;
            uint imageId = 1;
            for (var pageIndex = 0; pageIndex < plan.Pages.Count; pageIndex++)
            {
                var page = plan.Pages[pageIndex];
                foreach (var t in page.Text)
                {
                    var props = new W.RunProperties(new W.RunFonts { Ascii = plan.FontFamily, HighAnsi = plan.FontFamily });
                    if (t.Bold) props.Append(new W.Bold());
                    if (t.Italic) props.Append(new W.Italic());
                    props.Append(new W.FontSize { Val = ((int)Math.Round(t.FontSize * 2)).ToString(System.Globalization.CultureInfo.InvariantCulture) });
                    if (t.Underline) props.Append(new W.Underline { Val = W.UnderlineValues.Single });
                    var p = new W.Paragraph(new W.ParagraphProperties(Frame(t.X, t.Y, t.Width, t.Height),
                        new W.SpacingBetweenLines { Before = "0", After = "0", Line = Twips(t.Height), LineRule = W.LineSpacingRuleValues.Exact },
                        new W.Justification { Val = t.Alignment == TextAlignment.Center ? W.JustificationValues.Center : t.Alignment == TextAlignment.Right ? W.JustificationValues.Right : W.JustificationValues.Left }),
                        new W.Run(props, new W.Text(t.Text) { Space = SpaceProcessingModeValues.Preserve }));
                    body.Append(p);
                }
                foreach (var image in page.Images)
                {
                    var relation = "image" + imageId;
                    var part = main.AddImagePart(ImagePartType.Png, relation);
                    using (var stream = new MemoryStream(image.Png)) part.FeedData(stream);
                    var width = (long)Math.Round(image.Width * 12700); var height = (long)Math.Round(image.Height * 12700);
                    var drawing = new W.Drawing(new DW.Inline(
                        new DW.Extent { Cx = width, Cy = height }, new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                        new DW.DocProperties { Id = imageId, Name = image.Role + imageId },
                        new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                        new A.Graphic(new A.GraphicData(new PIC.Picture(
                            new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = imageId, Name = image.Role }, new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(new A.Blip { Embed = relation }, new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = width, Cy = height }), new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                            ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
                        { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 });
                    body.Append(new W.Paragraph(new W.ParagraphProperties(Frame(image.X, image.Y, image.Width, image.Height), new W.SpacingBetweenLines { Before = "0", After = "0" }), new W.Run(drawing)));
                    imageId++;
                }
                if (pageIndex + 1 < plan.Pages.Count) body.Append(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));
            }
            body.Append(new W.SectionProperties(new W.PageSize { Width = (uint)Math.Round(plan.Width * 20), Height = (uint)Math.Round(plan.Height * 20), Orient = W.PageOrientationValues.Portrait },
                new W.PageMargin { Top = (int)Math.Round(plan.Top * 20), Bottom = (int)Math.Round(plan.Bottom * 20), Left = (uint)Math.Round(plan.Left * 20), Right = (uint)Math.Round(plan.Right * 20), Header = 0, Footer = 0, Gutter = 0 }));
            main.Document.Save();
        }
        // OPC ZIP timestamps and entry order otherwise vary between generations.
        using var result = new MemoryStream();
        memory.Position = 0;
        using (var source = new ZipArchive(memory, ZipArchiveMode.Read, true))
        using (var target = new ZipArchive(result, ZipArchiveMode.Create, true))
            foreach (var entry in source.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                var copy = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var input = entry.Open(); using var output = copy.Open(); input.CopyTo(output);
            }
        return result.ToArray();
    }
    private static W.FrameProperties Frame(double x, double y, double width, double height) => new()
    {
        X = Twips(x), Y = Twips(y), Width = Twips(width), Height = (uint)Math.Round(height * 20),
        HorizontalPosition = W.HorizontalAnchorValues.Page, VerticalPosition = W.VerticalAnchorValues.Page,
        HeightType = W.HeightRuleValues.Exact, Wrap = W.TextWrappingValues.NotBeside
    };
    private static string Twips(double value) => ((int)Math.Round(value * 20)).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
