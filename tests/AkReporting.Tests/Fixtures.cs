using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using AkReporting.Application;
using AkReporting.Contracts;

namespace AkReporting.Tests;

internal static class Fixtures
{
    public static readonly DateTimeOffset Time = new(2026, 1, 2, 10, 15, 0, TimeSpan.FromHours(5.5));
    public static TemplateDefinition Template(string code) => CandidateTemplates.All().Single(t => t.ReportTypeCode == code);
    public static ReportRevision Revision(TemplateDefinition t, string name = "CBC-001")
    {
        var draft = new ReportDraft { TemplateVersionId = t.VersionId, Metadata = new ReportMetadata
        {
            Patient = new PatientMetadata { Name = "SYNTHETIC SOFTWARE FIXTURE " + name, LocalId = "QA-ONLY", Age = 35, AgeUnit = "years", Sex = "Not recorded" },
            ReportTime = Time, ClinicalHistory = "Software-only fixture. No clinical assertions."
        } };
        var sequence = 1;
        foreach (var s in t.Sections.Where(s => !s.Optional))
        foreach (var f in s.Fields)
            draft.Results.Add(new ReportResult { SectionCode = s.Code, FieldCode = f.Code, Kind = f.Kind,
                NumericValue = f.Kind == ResultKind.Numeric ? sequence++ * 3.125m : null,
                TextValue = f.Kind == ResultKind.Numeric ? "" : "Synthetic observation for layout QA only.", Unit = f.Unit, ReferenceText = f.ReferenceText });
        return new ReportRevision { Id = Guid.Parse("b0000000-0000-0000-0000-000000000001"), ReportId = Guid.Parse("b0000000-0000-0000-0000-000000000002"),
            CaseId = Guid.Parse("b0000000-0000-0000-0000-000000000003"), PublicNumber = "QA-2026-000001", Number = 1,
            CreatedAt = Time, State = ReportState.Draft, Reason = "Synthetic QA", Data = draft };
    }
    public static DoctorVersion Attribution() => new()
    {
        Id = Guid.Parse("b0000000-0000-0000-0000-000000000004"), DoctorId = Guid.Parse("b0000000-0000-0000-0000-000000000005"),
        Name = "Synthetic attribution fixture", DisplayName = "QA attribution block — not a clinician", Version = 1,
        PermissionEvidence = "Synthetic asset only", SignaturePng = Png(), StampPng = Png()
    };
    public static byte[] Png()
    {
        using var file = new MemoryStream();
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 64); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 20);
        header[8] = 8; header[9] = 2;
        Chunk(file, "IHDR", header);
        using var packed = new MemoryStream();
        using (var zip = new ZLibStream(packed, CompressionLevel.Optimal, true))
        for (var y = 0; y < 20; y++)
        {
            zip.WriteByte(0);
            for (var x = 0; x < 64; x++)
            {
                var color = (byte)(x is > 4 and < 60 && y is > 7 and < 12 ? 40 : 255);
                zip.Write([color, color, color]);
            }
        }
        Chunk(file, "IDAT", packed.ToArray()); Chunk(file, "IEND", []);
        return file.ToArray();
    }
    private static void Chunk(Stream file, string name, byte[] content)
    {
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(integer, (uint)content.Length); file.Write(integer);
        var type = Encoding.ASCII.GetBytes(name); file.Write(type); file.Write(content);
        var crc = 0xffffffffu;
        foreach (var value in type.Concat(content))
        {
            crc ^= value;
            for (var n = 0; n < 8; n++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(integer, ~crc); file.Write(integer);
    }
}
