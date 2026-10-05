using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Domain;

namespace AkReporting.Tests;

public sealed class DomainTests
{
    [Fact]
    public void RepresentativeDefinitionsHaveNoUnreviewedClinicalDefaults()
    {
        var definitions = CandidateTemplates.All();
        Assert.Equal(new[] { "CBC", "LFT", "URINE", "HISTO", "ECG" }, definitions.Select(t => t.ReportTypeCode));
        foreach (var t in definitions)
        {
            ReportValidator.Template(t);
            Assert.Equal(ReviewStatus.Draft, t.ReviewStatus);
            Assert.All(t.Sections.SelectMany(s => s.Fields), f => { Assert.Empty(f.Unit); Assert.Empty(f.ReferenceText); Assert.False(f.Required); });
            var d = Fixtures.Revision(t).Data;
            d.DoctorVersionId = Guid.NewGuid();
            Assert.Throws<ValidationException>(() => ReportValidator.Draft(d, t, true));
        }
    }
    [Fact]
    public void UnknownDuplicateAndMismatchedCellsCannotBeSaved()
    {
        var t = Fixtures.Template("CBC"); var d = Fixtures.Revision(t).Data;
        d.Results[0].Unit = "unreviewed";
        Assert.Throws<ValidationException>(() => ReportValidator.Draft(d, t));
        d.Results[0].Unit = ""; d.Results.Add(d.Results[0]);
        Assert.Throws<ValidationException>(() => ReportValidator.Draft(d, t));
        d.Results.RemoveAt(d.Results.Count - 1); d.Results[0].FieldCode = "unknown";
        Assert.Throws<ValidationException>(() => ReportValidator.Draft(d, t));
    }
    [Fact]
    public void RequiredAndDataBearingSectionsCannotBeHidden()
    {
        var t = Fixtures.Template("HISTO"); var d = Fixtures.Revision(t).Data;
        d.Formatting.HiddenSections.Add("comments"); ReportValidator.Draft(d, t);
        t.Sections.Last().Fields[0].Required = true;
        Assert.Throws<ValidationException>(() => ReportValidator.Draft(d, t));
        t.Sections.Last().Fields[0].Required = false;
        d.Results.Add(new ReportResult { SectionCode = "comments", FieldCode = "comment", Kind = ResultKind.Multiline, TextValue = "Synthetic comment" });
        Assert.Throws<ValidationException>(() => ReportValidator.Draft(d, t));
    }
    [Fact]
    public void QualitativeVocabularyAndRepeatedGroupCoordinatesAreEnforced()
    {
        var t = Fixtures.Template("URINE"); var s = t.Sections[0]; var f = s.Fields[0];
        f.Kind = ResultKind.Coded; f.Choices = ["QA-A", "QA-B"]; s.Repeatable = true;
        var d = Fixtures.Revision(Fixtures.Template("URINE")).Data;
        var r = d.Results[0]; r.Kind = ResultKind.Coded; r.TextValue = "QA-A"; r.Row = 2;
        ReportValidator.Draft(d, t);
        r.TextValue = "not-reviewed"; Assert.Throws<ValidationException>(() => ReportValidator.Draft(d, t));
        r.TextValue = "QA-A"; s.Repeatable = false; Assert.Throws<ValidationException>(() => ReportValidator.Draft(d, t));
    }
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(100)]
    public void InvalidFormattingFailsBeforeRender(double size)
    {
        var t = Fixtures.Template("CBC"); var d = Fixtures.Revision(t).Data; d.Formatting.FontSize = size;
        Assert.Throws<ValidationException>(() => ReportValidator.Draft(d, t));
    }
    [Fact]
    public void ClinicalProvenanceAndReviewerEvidenceAreRequired()
    {
        var t = Fixtures.Template("CBC"); t.Sections[0].Fields[0].Unit = "configuration-test-only";
        Assert.Throws<ValidationException>(() => ReportValidator.Template(t));
        t.Sections[0].Fields[0].ClinicalSource = "Software validator fixture, not clinical approval";
        t.ReviewStatus = ReviewStatus.Approved;
        Assert.Throws<ValidationException>(() => ReportValidator.Template(t));
    }
    [Fact]
    public void InvalidUnicodeAndCollectionChronologyAreRejected()
    {
        var t = Fixtures.Template("CBC"); var d = Fixtures.Revision(t).Data;
        d.Metadata.Patient.Name = "Invalid \ud800 sequence";
        Assert.Throws<ValidationException>(() => ReportValidator.Draft(d, t));
        d.Metadata.Patient.Name = "Synthetic patient"; d.Metadata.CollectionTime = d.Metadata.ReportTime.AddMinutes(1);
        Assert.Throws<ValidationException>(() => ReportValidator.Draft(d, t));
        d.Metadata.CollectionTime = null; d.Metadata.Patient.Age = 365; d.Metadata.Patient.AgeUnit = "days";
        ReportValidator.Draft(d, t);
    }
}
