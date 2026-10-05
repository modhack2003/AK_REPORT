using System;
using System.Collections.Generic;

namespace AkReporting.Contracts
{
    public enum ResultKind { Numeric, Text, Qualitative, PositiveNegative, Coded, Multiline }
    public enum SectionLayout { ResultTable, Narrative, StructuredGroup }
    public enum ReviewStatus { Draft, Approved }
    public enum ReportState { Draft, Issued }
    public enum RetentionState { Active, Archived, Expired }
    public enum TextAlignment { Left, Center, Right }

    public sealed class FieldDefinition
    {
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public ResultKind Kind { get; set; }
        public bool Required { get; set; }
        public bool AllowComparator { get; set; }
        public string Unit { get; set; } = "";
        public string ReferenceText { get; set; } = "";
        public string ClinicalSource { get; set; } = "";
        public int MaxLength { get; set; } = 2000;
        public List<string> Choices { get; set; } = new List<string>();
    }
    public sealed class SectionDefinition
    {
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public SectionLayout Layout { get; set; }
        public bool Optional { get; set; }
        public bool Repeatable { get; set; }
        public bool PageBreakBefore { get; set; }
        public List<FieldDefinition> Fields { get; set; } = new List<FieldDefinition>();
    }
    public sealed class LayoutDefinition
    {
        public double TopMm { get; set; } = 40;
        public double BottomMm { get; set; } = 25;
        public double LeftMm { get; set; } = 15;
        public double RightMm { get; set; } = 15;
        public double FontSize { get; set; } = 10;
        public string FontFamily { get; set; } = "Noto Sans";
    }
    public sealed class TemplateDefinition
    {
        public Guid VersionId { get; set; }
        public string ReportTypeCode { get; set; } = "";
        public string Title { get; set; } = "";
        public int Version { get; set; }
        public ReviewStatus ReviewStatus { get; set; }
        public string ReviewEvidence { get; set; } = "";
        public string ReviewedBy { get; set; } = "";
        public DateTimeOffset? ReviewedAt { get; set; }
        public LayoutDefinition Layout { get; set; } = new LayoutDefinition();
        public List<SectionDefinition> Sections { get; set; } = new List<SectionDefinition>();
    }
    public sealed class PatientMetadata
    {
        public string Name { get; set; } = "";
        public string LocalId { get; set; } = "";
        public int? Age { get; set; }
        public string AgeUnit { get; set; } = "";
        public string Sex { get; set; } = "";
    }
    public sealed class ReportMetadata
    {
        public PatientMetadata Patient { get; set; } = new PatientMetadata();
        public string ReferringDoctor { get; set; } = "";
        public string ClinicalHistory { get; set; } = "";
        public string Specimen { get; set; } = "";
        public string SpecimenId { get; set; } = "";
        public string Method { get; set; } = "";
        public string TechnicianAttribution { get; set; } = "";
        public DateTimeOffset? CollectionTime { get; set; }
        public DateTimeOffset ReportTime { get; set; }
    }
    public sealed class ReportResult
    {
        public string SectionCode { get; set; } = "";
        public string FieldCode { get; set; } = "";
        public int Row { get; set; }
        public ResultKind Kind { get; set; }
        public decimal? NumericValue { get; set; }
        public string Comparator { get; set; } = "";
        public string TextValue { get; set; } = "";
        public string Unit { get; set; } = "";
        public string ReferenceText { get; set; } = "";
    }
    public sealed class ReportFormatting
    {
        public string FontFamily { get; set; } = "Noto Sans";
        public double FontSize { get; set; } = 10;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public TextAlignment Alignment { get; set; }
        public List<string> HiddenSections { get; set; } = new List<string>();
    }
    public sealed class ReportDraft
    {
        public Guid TemplateVersionId { get; set; }
        public Guid? DoctorVersionId { get; set; }
        public ReportMetadata Metadata { get; set; } = new ReportMetadata();
        public ReportFormatting Formatting { get; set; } = new ReportFormatting();
        public List<ReportResult> Results { get; set; } = new List<ReportResult>();
    }
    public sealed class CreateCaseRequest
    {
        public Guid OperationId { get; set; }
        public PatientMetadata Patient { get; set; } = new PatientMetadata();
    }
    public sealed class CaseSummary
    {
        public Guid Id { get; set; }
        public PatientMetadata Patient { get; set; } = new PatientMetadata();
        public DateTimeOffset CreatedAt { get; set; }
    }
    public sealed class CaseSearchRequest
    {
        public string Query { get; set; } = "";
        public int Offset { get; set; }
        public int Limit { get; set; } = 100;
    }
    public sealed class CreateReportRequest
    {
        public Guid OperationId { get; set; }
        public Guid CaseId { get; set; }
        public ReportDraft Draft { get; set; } = new ReportDraft();
    }
    public sealed class SaveRevisionRequest
    {
        public int ExpectedRevision { get; set; }
        public string Reason { get; set; } = "";
        public ReportDraft Draft { get; set; } = new ReportDraft();
    }
    public sealed class IssueReportRequest
    {
        public int ExpectedRevision { get; set; }
        public string Reason { get; set; } = "";
        public string AuthorizationBasis { get; set; } = "";
    }
    public sealed class ReportRevision
    {
        public Guid Id { get; set; }
        public Guid ReportId { get; set; }
        public Guid CaseId { get; set; }
        public string PublicNumber { get; set; } = "";
        public int Number { get; set; }
        public Guid? PreviousRevisionId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public ReportState State { get; set; }
        public string Reason { get; set; } = "";
        public string AuthorizationBasis { get; set; } = "";
        public List<string> ChangedPaths { get; set; } = new List<string>();
        public ReportDraft Data { get; set; } = new ReportDraft();
    }
    public sealed class ReportSummary
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }
        public string PublicNumber { get; set; } = "";
        public string TypeCode { get; set; } = "";
        public int CurrentRevision { get; set; }
        public RetentionState Retention { get; set; }
    }
}
