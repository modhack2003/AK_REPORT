using System.Buffers.Binary;
using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Domain;
using PdfSharp.Drawing;

namespace AkReporting.Rendering;

public sealed class ReportRenderer : IReportRenderer
{
    public PagePlan Plan(ReportRevision revision, TemplateDefinition template, DoctorVersion? doctor)
    {
        ValidateImage(doctor?.SignaturePng); ValidateImage(doctor?.StampPng);
        return new LayoutEngine().Build(revision, template, doctor);
    }
    public RenderedDocument Render(PagePlan plan, string format, DateTimeOffset timestamp)
    {
        if (plan.Pages.Count is < 1 or > 100) throw new ValidationException("Invalid page plan.");
        var bytes = format switch { "pdf" => PdfOutput.Write(plan, timestamp), "docx" => DocxOutput.Write(plan, timestamp), _ => throw new ValidationException("Output format must be pdf or docx.") };
        return new RenderedDocument(bytes, format, plan);
    }
    public void ValidateImage(byte[]? png)
    {
        if (png == null) return;
        if (png.Length is < 33 or > 2097152 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new ValidationException("Invalid PNG signature/stamp.");
        var width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        if (width is 0 or > 2048 || height is 0 or > 2048) throw new ValidationException("Signature/stamp exceeds 2048-pixel limits.");
        try
        {
            using var decoded = XImage.FromStream(new MemoryStream(png));
            if (decoded.PixelWidth != width || decoded.PixelHeight != height) throw new ValidationException("PNG dimensions differ from decoded image.");
        }
        catch (Exception ex) when (ex is not ValidationException) { throw new ValidationException("PNG signature/stamp could not be decoded."); }
    }
}
