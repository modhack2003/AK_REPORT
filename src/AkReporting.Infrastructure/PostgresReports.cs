using System.Text.Json;
using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Domain;
using Npgsql;
using static AkReporting.Infrastructure.Database;

namespace AkReporting.Infrastructure;

public sealed class PostgresReports(Database db, TimeProvider clock) : IReportRepository
{
    public async Task<CaseSummary> CreateCase(CreateCaseRequest request, Actor actor, CancellationToken ct)
    {
        ReportValidator.Patient(request.Patient);
        if (request.OperationId == Guid.Empty) throw new ValidationException("Operation ID is required.");
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperation(c, tx, request.OperationId, ct);
        await using (var existing = Command(c, tx, "SELECT id,request_hash FROM report_case WHERE operation_id=@op", ("op", request.OperationId)))
        {
            await using var r = await existing.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                if (r.GetString(1) != Hash(Json(request))) throw new ConflictException("Operation ID was reused with different case data.");
                var id = r.GetGuid(0);
                await r.DisposeAsync();
                var found = await ReadCase(c, tx, id, ct);
                await tx.CommitAsync(ct);
                return found;
            }
        }
        var patientId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var p = request.Patient;
        await using (var patient = Command(c, tx, "INSERT INTO patient VALUES (@id,@name,@local,@age,@unit,@sex)",
            ("id", patientId), ("name", p.Name), ("local", p.LocalId), ("age", p.Age), ("unit", p.AgeUnit), ("sex", p.Sex))) await patient.ExecuteNonQueryAsync(ct);
        var time = clock.GetUtcNow();
        await using (var cmd = Command(c, tx, "INSERT INTO report_case VALUES (@id,@patient,@op,@hash,@actor,@time)",
            ("id", caseId), ("patient", patientId), ("op", request.OperationId), ("hash", Hash(Json(request))), ("actor", actor.Id), ("time", time.UtcDateTime))) await cmd.ExecuteNonQueryAsync(ct);
        await Audit(c, tx, actor, "case.create", caseId, null, ct);
        await tx.CommitAsync(ct);
        return new CaseSummary { Id = caseId, Patient = p, CreatedAt = time };
    }
    public async Task<IReadOnlyList<CaseSummary>> Cases(CancellationToken ct)
        => await SearchCases(new CaseSearchRequest(), ct);
    public async Task<IReadOnlyList<CaseSummary>> SearchCases(CaseSearchRequest request, CancellationToken ct)
    {
        ReportValidator.Bounded(request.Query, "Search query", 120);
        if (request.Offset is < 0 or > 100000 || request.Limit is < 1 or > 100) throw new ValidationException("Invalid search page.");
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var cmd = Command(c, null, CaseSelect +
            " WHERE @query='' OR position(lower(@query) in lower(p.name))>0 OR position(lower(@query) in lower(p.local_id))>0 OR EXISTS (SELECT 1 FROM diagnostic_report d WHERE d.case_id=c.id AND position(lower(@query) in lower(d.public_number))>0) ORDER BY c.created_at DESC,c.id LIMIT @limit OFFSET @offset",
            ("query", request.Query.Trim()), ("limit", request.Limit), ("offset", request.Offset));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var result = new List<CaseSummary>();
        while (await r.ReadAsync(ct)) result.Add(CaseRow(r));
        return result;
    }
    public async Task<IReadOnlyList<ReportSummary>> Reports(Guid caseId, CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var cmd = Command(c, null,
            "SELECT d.id,d.case_id,d.public_number,d.report_type_code,r.revision_number,d.retention_state FROM diagnostic_report d JOIN report_revision r ON r.id=d.current_revision_id WHERE d.case_id=@case ORDER BY d.public_number",
            ("case", caseId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var result = new List<ReportSummary>();
        while (await r.ReadAsync(ct)) result.Add(new ReportSummary { Id = r.GetGuid(0), CaseId = r.GetGuid(1), PublicNumber = r.GetString(2), TypeCode = r.GetString(3), CurrentRevision = r.GetInt32(4), Retention = (RetentionState)r.GetInt32(5) });
        return result;
    }
    public async Task<ReportRevision> CreateReport(CreateReportRequest request, Actor actor, CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperation(c, tx, request.OperationId, ct);
        await using (var existing = Command(c, tx, "SELECT id,request_hash FROM diagnostic_report WHERE operation_id=@op", ("op", request.OperationId)))
        {
            await using var r = await existing.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                if (r.GetString(1) != Hash(Json(request))) throw new ConflictException("Operation ID was reused with different report data.");
                var id = r.GetGuid(0);
                await r.DisposeAsync();
                var found = await ReadRevision(c, tx, id, 1, ct);
                await tx.CommitAsync(ct);
                return found;
            }
        }
        var caseData = await ReadCase(c, tx, request.CaseId, ct);
        if (Json(caseData.Patient) != Json(request.Draft.Metadata.Patient)) throw new ValidationException("New report header must match the selected case patient.");
        var template = await ReadTemplate(c, tx, request.Draft.TemplateVersionId, ct);
        ReportValidator.Draft(request.Draft, template);
        await CheckActiveDoctor(c, tx, request.Draft.DoctorVersionId, ct);
        var time = clock.GetUtcNow();
        await using var settings = new NpgsqlCommand("SELECT report_prefix,retention_days FROM settings WHERE singleton", c, tx);
        string prefix;
        int retention;
        await using (var r = await settings.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) throw new IntegrityException("Settings missing.");
            prefix = r.GetString(0); retention = r.GetInt32(1);
        }
        var year = time.ToOffset(TimeSpan.FromMinutes(330)).Year;
        await using var counter = Command(c, tx,
            "INSERT INTO report_number_counter VALUES (@prefix,@year,1) ON CONFLICT(prefix,year) DO UPDATE SET last_value=report_number_counter.last_value+1 RETURNING last_value",
            ("prefix", prefix), ("year", year));
        var sequence = (long)(await counter.ExecuteScalarAsync(ct))!;
        var number = $"{prefix}-{year}-{sequence:D6}";
        var reportId = Guid.NewGuid();
        await using (var cmd = Command(c, tx,
            "INSERT INTO diagnostic_report VALUES (@id,@case,@type,@number,@op,@hash,NULL,0,@time,@expires)",
            ("id", reportId), ("case", request.CaseId), ("type", template.ReportTypeCode), ("number", number),
            ("op", request.OperationId), ("hash", Hash(Json(request))), ("time", time.UtcDateTime), ("expires", time.AddDays(retention).UtcDateTime))) await cmd.ExecuteNonQueryAsync(ct);
        var revision = new ReportRevision { Id = Guid.NewGuid(), ReportId = reportId, CaseId = request.CaseId,
            PublicNumber = number, Number = 1, CreatedAt = time, CreatedBy = actor.Id, State = ReportState.Draft,
            Reason = "Initial creation", ChangedPaths = ["initial"], Data = request.Draft };
        await InsertRevision(c, tx, revision, template, actor, ct);
        await tx.CommitAsync(ct);
        return revision;
    }
    public async Task<ReportRevision> Revision(Guid reportId, int? number, CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        return await ReadRevision(c, null, reportId, number, ct);
    }
    public async Task<IReadOnlyList<ReportRevision>> History(Guid reportId, CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        var numbers = new List<int>();
        await using (var cmd = Command(c, null, "SELECT revision_number FROM report_revision WHERE report_id=@id ORDER BY revision_number", ("id", reportId)))
        await using (var r = await cmd.ExecuteReaderAsync(ct)) while (await r.ReadAsync(ct)) numbers.Add(r.GetInt32(0));
        if (numbers.Count == 0) throw new NotFoundException("Report not found.");
        var history = new List<ReportRevision>();
        foreach (var number in numbers) history.Add(await ReadRevision(c, null, reportId, number, ct));
        return history;
    }
    public async Task<ReportRevision> Append(Guid reportId, int expected, ReportDraft draft, string reason, ReportState state, string authorizationBasis, Actor actor, CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using (var locked = Command(c, tx, "SELECT id FROM diagnostic_report WHERE id=@id FOR UPDATE", ("id", reportId)))
            if (await locked.ExecuteScalarAsync(ct) == null) throw new NotFoundException("Report not found.");
        var previous = await ReadRevision(c, tx, reportId, null, ct);
        if (previous.Number != expected) throw new ConflictException("Another writer saved this report. Reload and reconcile your correction.");
        var template = await ReadTemplate(c, tx, draft.TemplateVersionId, ct);
        ReportValidator.Draft(draft, template, state == ReportState.Issued);
        await CheckActiveDoctor(c, tx, draft.DoctorVersionId, ct);
        var revision = new ReportRevision { Id = Guid.NewGuid(), ReportId = reportId, CaseId = previous.CaseId,
            PublicNumber = previous.PublicNumber, Number = expected + 1, PreviousRevisionId = previous.Id,
            CreatedAt = clock.GetUtcNow(), CreatedBy = actor.Id, State = state, Reason = reason,
            AuthorizationBasis = authorizationBasis, ChangedPaths = Changes(previous.Data, draft), Data = draft };
        if (state != previous.State) revision.ChangedPaths.Add("state");
        await InsertRevision(c, tx, revision, template, actor, ct);
        await tx.CommitAsync(ct);
        return revision;
    }
    public async Task SetRetention(Guid reportId, RetentionState state, Actor actor, CancellationToken ct)
    {
        if (!Enum.IsDefined(state)) throw new ValidationException("Unknown retention state.");
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using var cmd = Command(c, tx, "UPDATE diagnostic_report SET retention_state=@state WHERE id=@id", ("state", (int)state), ("id", reportId));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new NotFoundException("Report not found.");
        await Audit(c, tx, actor, "report.retention", reportId, null, ct);
        await tx.CommitAsync(ct);
    }
    private static async Task InsertRevision(NpgsqlConnection c, NpgsqlTransaction tx, ReportRevision r, TemplateDefinition t, Actor actor, CancellationToken ct)
    {
        await using (var cmd = Command(c, tx,
            "INSERT INTO report_revision VALUES (@id,@report,@number,@previous,@template,@doctor,CAST(@metadata AS jsonb),CAST(@formatting AS jsonb),@state,@reason,@basis,CAST(@changes AS jsonb),@actor,@time)",
            ("id", r.Id), ("report", r.ReportId), ("number", r.Number), ("previous", r.PreviousRevisionId),
            ("template", r.Data.TemplateVersionId), ("doctor", r.Data.DoctorVersionId), ("metadata", Json(r.Data.Metadata)),
            ("formatting", Json(r.Data.Formatting)), ("state", (int)r.State), ("reason", r.Reason), ("basis", r.AuthorizationBasis),
            ("changes", Json(r.ChangedPaths)), ("actor", actor.Id), ("time", r.CreatedAt.UtcDateTime))) await cmd.ExecuteNonQueryAsync(ct);
        foreach (var section in t.Sections)
        {
            var id = Guid.NewGuid();
            await using (var cmd = Command(c, tx, "INSERT INTO report_section VALUES (@id,@revision,@code,@visible)",
                ("id", id), ("revision", r.Id), ("code", section.Code), ("visible", !r.Data.Formatting.HiddenSections.Contains(section.Code)))) await cmd.ExecuteNonQueryAsync(ct);
            foreach (var value in r.Data.Results.Where(x => x.SectionCode == section.Code))
            {
                await using var cmd = Command(c, tx, "INSERT INTO report_result VALUES (@section,@field,@row,@kind,@numeric,@text,@comparator,@unit,@reference)",
                    ("section", id), ("field", value.FieldCode), ("row", value.Row), ("kind", (int)value.Kind),
                    ("numeric", value.NumericValue), ("text", value.TextValue), ("comparator", value.Comparator), ("unit", value.Unit), ("reference", value.ReferenceText));
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        await using (var current = Command(c, tx, "UPDATE diagnostic_report SET current_revision_id=@revision WHERE id=@id", ("revision", r.Id), ("id", r.ReportId))) await current.ExecuteNonQueryAsync(ct);
        await Audit(c, tx, actor, r.State == ReportState.Issued ? "report.issue" : "report.save", r.ReportId, r.Number, ct);
    }
    private static async Task<ReportRevision> ReadRevision(NpgsqlConnection c, NpgsqlTransaction? tx, Guid reportId, int? number, CancellationToken ct)
    {
        var selector = number.HasValue ? "r.revision_number=@number" : "r.id=d.current_revision_id";
        await using var cmd = Command(c, tx,
            "SELECT r.id,d.case_id,d.public_number,r.revision_number,r.previous_revision_id,r.template_version_id,r.doctor_version_id,r.metadata::text,r.formatting::text,r.state,r.reason,r.authorization_basis,r.changed_paths::text,r.created_by,r.created_at FROM report_revision r JOIN diagnostic_report d ON d.id=r.report_id WHERE d.id=@id AND " + selector,
            ("id", reportId), ("number", number ?? 0));
        ReportRevision revision;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) throw new NotFoundException("Report revision not found.");
            revision = new ReportRevision { Id = r.GetGuid(0), ReportId = reportId, CaseId = r.GetGuid(1), PublicNumber = r.GetString(2),
                Number = r.GetInt32(3), PreviousRevisionId = r.IsDBNull(4) ? null : r.GetGuid(4),
                Data = new ReportDraft { TemplateVersionId = r.GetGuid(5), DoctorVersionId = r.IsDBNull(6) ? null : r.GetGuid(6),
                    Metadata = FromJson<ReportMetadata>(r.GetString(7)), Formatting = FromJson<ReportFormatting>(r.GetString(8)) },
                State = (ReportState)r.GetInt32(9), Reason = r.GetString(10), AuthorizationBasis = r.GetString(11),
                ChangedPaths = FromJson<List<string>>(r.GetString(12)), CreatedBy = r.GetGuid(13), CreatedAt = r.GetFieldValue<DateTimeOffset>(14) };
        }
        await using var results = Command(c, tx, "SELECT s.code,v.field_code,v.row_index,v.kind,v.numeric_value,v.text_value,v.comparator,v.unit,v.reference_text FROM report_section s JOIN report_result v ON v.section_id=s.id WHERE s.revision_id=@id ORDER BY s.code,v.row_index,v.field_code", ("id", revision.Id));
        await using var cells = await results.ExecuteReaderAsync(ct);
        while (await cells.ReadAsync(ct)) revision.Data.Results.Add(new ReportResult { SectionCode = cells.GetString(0), FieldCode = cells.GetString(1),
            Row = cells.GetInt32(2), Kind = (ResultKind)cells.GetInt32(3), NumericValue = cells.IsDBNull(4) ? null : cells.GetDecimal(4),
            TextValue = cells.GetString(5), Comparator = cells.GetString(6), Unit = cells.GetString(7), ReferenceText = cells.GetString(8) });
        return revision;
    }
    private const string CaseSelect = "SELECT c.id,c.created_at,p.name,p.local_id,p.age,p.age_unit,p.sex FROM report_case c JOIN patient p ON p.id=c.patient_id";
    private static CaseSummary CaseRow(NpgsqlDataReader r) => new() { Id = r.GetGuid(0), CreatedAt = r.GetFieldValue<DateTimeOffset>(1),
        Patient = new PatientMetadata { Name = r.GetString(2), LocalId = r.GetString(3), Age = r.IsDBNull(4) ? null : r.GetInt32(4), AgeUnit = r.GetString(5), Sex = r.GetString(6) } };
    private static async Task<CaseSummary> ReadCase(NpgsqlConnection c, NpgsqlTransaction? tx, Guid id, CancellationToken ct)
    {
        await using var cmd = Command(c, tx, CaseSelect + " WHERE c.id=@id", ("id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw new NotFoundException("Case not found.");
        return CaseRow(r);
    }
    private static async Task<TemplateDefinition> ReadTemplate(NpgsqlConnection c, NpgsqlTransaction tx, Guid id, CancellationToken ct)
    {
        await using var cmd = Command(c, tx, "SELECT definition::text FROM report_template_version WHERE id=@id", ("id", id));
        return FromJson<TemplateDefinition>(await cmd.ExecuteScalarAsync(ct) as string ?? throw new NotFoundException("Template version not found."));
    }
    private static async Task LockOperation(NpgsqlConnection c, NpgsqlTransaction tx, Guid id, CancellationToken ct)
    {
        await using var cmd = Command(c, tx, "SELECT pg_advisory_xact_lock(hashtext(@key))", ("key", id.ToString()));
        await cmd.ExecuteNonQueryAsync(ct);
    }
    private static async Task CheckActiveDoctor(NpgsqlConnection c, NpgsqlTransaction tx, Guid? version, CancellationToken ct)
    {
        if (version == null) return;
        await using var cmd = Command(c, tx,
            "SELECT d.active FROM doctor d JOIN doctor_signature_version v ON v.doctor_id=d.id WHERE v.id=@version FOR SHARE OF d", ("version", version));
        var active = await cmd.ExecuteScalarAsync(ct);
        if (active == null) throw new NotFoundException("Doctor version not found.");
        if (!(bool)active) throw new ValidationException("Selected doctor is inactive.");
    }
    private static List<string> Changes(ReportDraft a, ReportDraft b)
    {
        var left = Flatten(a); var right = Flatten(b);
        return left.Keys.Union(right.Keys).Where(k => left.GetValueOrDefault(k) != right.GetValueOrDefault(k)).Order(StringComparer.Ordinal).ToList();
    }
    private static Dictionary<string, string> Flatten(ReportDraft data)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        void Walk(JsonElement element, string path)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var p in element.EnumerateObject()) Walk(p.Value, path + "/" + p.Name);
            else values[path] = element.GetRawText();
        }
        Walk(JsonSerializer.SerializeToElement(data.Metadata), "metadata");
        Walk(JsonSerializer.SerializeToElement(data.Formatting), "formatting");
        values["templateVersionId"] = data.TemplateVersionId.ToString();
        values["doctorVersionId"] = data.DoctorVersionId.ToString() ?? "";
        foreach (var r in data.Results) Walk(JsonSerializer.SerializeToElement(r), $"results/{r.SectionCode}/{r.FieldCode}/{r.Row}");
        return values;
    }
}
