using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AkReporting.Contracts;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace AkReporting.Rendering;

public static partial class PdfOutput
{
    public static byte[] Write(PagePlan plan, DateTimeOffset timestamp)
    {
        using var document = new PdfDocument();
        document.Info.Creator = plan.EngineVersion;
        document.Info.CreationDate = timestamp.UtcDateTime;
        document.Info.ModificationDate = timestamp.UtcDateTime;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plan)));
        document.Internals.FirstDocumentID = Encoding.Latin1.GetString(hash.AsSpan(0, 16));
        document.Internals.SecondDocumentID = document.Internals.FirstDocumentID;
        foreach (var page in plan.Pages)
        {
            var pdfPage = document.AddPage();
            pdfPage.Width = XUnit.FromPoint(plan.Width); pdfPage.Height = XUnit.FromPoint(plan.Height);
            using var graphics = XGraphics.FromPdfPage(pdfPage);
            foreach (var t in page.Text.Where(x => x.Text.Length > 0))
            {
                var format = t.Alignment switch { TextAlignment.Center => XStringFormats.TopCenter, TextAlignment.Right => XStringFormats.TopRight, _ => XStringFormats.TopLeft };
                var font = ReportFonts.Font(t.FontSize, t.Bold, t.Italic, plan.FontFamily);
                graphics.DrawString(t.Text, font, XBrushes.Black, new XRect(t.X, t.Y, t.Width, t.Height), format);
                if (t.Underline)
                {
                    var width = graphics.MeasureString(t.Text, font).Width;
                    var x = t.Alignment == TextAlignment.Center ? t.X + (t.Width - width) / 2 : t.Alignment == TextAlignment.Right ? t.X + t.Width - width : t.X;
                    graphics.DrawLine(XPens.Black, x, t.Y + t.FontSize * 1.4, x + width, t.Y + t.FontSize * 1.4);
                }
            }
            foreach (var image in page.Images)
            {
                using var png = XImage.FromStream(new MemoryStream(image.Png));
                graphics.DrawImage(png, image.X, image.Y, image.Width, image.Height);
            }
        }
        using var memory = new MemoryStream();
        document.Save(memory, false);
        // PDFsharp randomizes font subset prefixes. Replace only dictionary names with
        // equal-length deterministic prefixes; byte offsets/xref entries stay intact.
        var text = Encoding.Latin1.GetString(memory.ToArray());
        text = SubsetNames().Replace(text, match =>
        {
            var digest = SHA256.HashData(Encoding.ASCII.GetBytes(match.Groups[4].Value));
            var prefix = new string(digest.Take(6).Select(x => (char)('A' + x % 26)).ToArray());
            return "/" + match.Groups[1].Value + match.Groups[2].Value + "/" + prefix + "+" + match.Groups[4].Value;
        });
        // PDFsharp also writes fresh XMP UUIDs on each save. XMP is uncompressed;
        // these fixed-size substitutions preserve its stream length and xref offsets.
        var stableGuid = new Guid(hash.AsSpan(0, 16)).ToString("D");
        text = XmpIdentifiers().Replace(text, match => match.Groups[1].Value + stableGuid);
        return Encoding.Latin1.GetBytes(text);
    }
    [GeneratedRegex(@"/(BaseFont|FontName)(\s*)/([A-Z]{6})\+([^ /\r\n]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SubsetNames();
    [GeneratedRegex(@"(<xmpMM:(?:DocumentID|InstanceID)>uuid:)[0-9a-fA-F-]{36}", RegexOptions.CultureInvariant)]
    private static partial Regex XmpIdentifiers();
}
