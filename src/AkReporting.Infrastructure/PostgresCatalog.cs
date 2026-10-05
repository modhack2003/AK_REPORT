using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Domain;
using Npgsql;
using static AkReporting.Infrastructure.Database;

namespace AkReporting.Infrastructure;

public sealed class PostgresCatalog(Database db, TimeProvider clock) : ICatalogRepository
{
    public async Task<IReadOnlyList<TemplateDefinition>> Templates(CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT definition::text FROM report_template_version ORDER BY template_id,version", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var items = new List<TemplateDefinition>();
        while (await r.ReadAsync(ct)) items.Add(FromJson<TemplateDefinition>(r.GetString(0)));
        return items;
    }
    public async Task<TemplateDefinition> Template(Guid id, CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var cmd = Command(c, null, "SELECT definition::text,content_hash FROM report_template_version WHERE id=@id", ("id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw new NotFoundException("Template version not found.");
        var t = FromJson<TemplateDefinition>(r.GetString(0));
        if (Hash(Json(t)) != r.GetString(1)) throw new IntegrityException("Template hash mismatch.");
        return t;
    }
    public async Task<TemplateDefinition> AddTemplate(TemplateDefinition definition, Actor actor, CancellationToken ct)
    {
        ReportValidator.Template(definition);
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using (var guard = Command(c, tx, "SELECT pg_advisory_xact_lock(hashtext(@code))", ("code", definition.ReportTypeCode))) await guard.ExecuteNonQueryAsync(ct);
        await using (var type = Command(c, tx, "INSERT INTO report_type VALUES (@code,@title) ON CONFLICT DO NOTHING", ("code", definition.ReportTypeCode), ("title", definition.Title))) await type.ExecuteNonQueryAsync(ct);
        await using (var create = Command(c, tx, "INSERT INTO report_template VALUES (@id,@code) ON CONFLICT DO NOTHING", ("id", Guid.NewGuid()), ("code", definition.ReportTypeCode))) await create.ExecuteNonQueryAsync(ct);
        await using var parent = Command(c, tx, "SELECT id FROM report_template WHERE report_type_code=@code", ("code", definition.ReportTypeCode));
        var templateId = (Guid)(await parent.ExecuteScalarAsync(ct))!;
        await using var next = Command(c, tx, "SELECT COALESCE(MAX(version),0)+1 FROM report_template_version WHERE template_id=@id", ("id", templateId));
        var number = (int)(await next.ExecuteScalarAsync(ct))!;
        if (definition.Version != number) throw new ConflictException($"Next template version must be {number}.");
        await using var cmd = Command(c, tx,
            "INSERT INTO report_template_version VALUES (@id,@parent,@version,CAST(@json AS jsonb),@hash,@status,@evidence,@reviewer,@time,@actor,@created)",
            ("id", definition.VersionId), ("parent", templateId), ("version", definition.Version), ("json", Json(definition)),
            ("hash", Hash(Json(definition))), ("status", (int)definition.ReviewStatus), ("evidence", definition.ReviewEvidence),
            ("reviewer", definition.ReviewedBy), ("time", definition.ReviewedAt?.UtcDateTime ?? (object)DBNull.Value),
            ("actor", actor.Id), ("created", clock.GetUtcNow().UtcDateTime));
        await cmd.ExecuteNonQueryAsync(ct);
        await Audit(c, tx, actor, "template.version", definition.VersionId, null, ct);
        await tx.CommitAsync(ct);
        return definition;
    }
    public async Task<IReadOnlyList<DoctorVersion>> Doctors(CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT DISTINCT ON (v.doctor_id) v.profile::text,d.active FROM doctor_signature_version v JOIN doctor d ON d.id=v.doctor_id ORDER BY v.doctor_id,v.version DESC", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var doctors = new List<DoctorVersion>();
        while (await r.ReadAsync(ct))
        {
            var d = FromJson<DoctorVersion>(r.GetString(0));
            d.Active = r.GetBoolean(1);
            doctors.Add(d);
        }
        return doctors;
    }
    public async Task<DoctorVersion> Doctor(Guid id, CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var cmd = Command(c, null, "SELECT v.profile::text,v.signature_png,v.stamp_png,d.active,v.content_hash FROM doctor_signature_version v JOIN doctor d ON d.id=v.doctor_id WHERE v.id=@id", ("id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw new NotFoundException("Doctor version not found.");
        var d = FromJson<DoctorVersion>(r.GetString(0));
        d.SignaturePng = r.IsDBNull(1) ? null : r.GetFieldValue<byte[]>(1);
        d.StampPng = r.IsDBNull(2) ? null : r.GetFieldValue<byte[]>(2);
        if (Hash(Json(d)) != r.GetString(4)) throw new IntegrityException("Doctor profile/asset hash mismatch.");
        d.Active = r.GetBoolean(3);
        return d;
    }
    public async Task<DoctorVersion> AddDoctorVersion(DoctorVersion input, Actor actor, CancellationToken ct)
    {
        var d = FromJson<DoctorVersion>(Json(input));
        ReportValidator.Doctor(d);
        d.Id = Guid.NewGuid();
        if (d.DoctorId == Guid.Empty) d.DoctorId = Guid.NewGuid();
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using (var guard = Command(c, tx, "SELECT pg_advisory_xact_lock(hashtext(@key))", ("key", d.DoctorId.ToString()))) await guard.ExecuteNonQueryAsync(ct);
        await using (var parent = Command(c, tx, "INSERT INTO doctor VALUES (@id,@active) ON CONFLICT DO NOTHING", ("id", d.DoctorId), ("active", d.Active))) await parent.ExecuteNonQueryAsync(ct);
        await using var next = Command(c, tx, "SELECT COALESCE(MAX(version),0)+1 FROM doctor_signature_version WHERE doctor_id=@id", ("id", d.DoctorId));
        d.Version = (int)(await next.ExecuteScalarAsync(ct))!;
        var hash = Hash(Json(d));
        var signature = d.SignaturePng;
        var stamp = d.StampPng;
        d.SignaturePng = null;
        d.StampPng = null;
        await using var cmd = Command(c, tx,
            "INSERT INTO doctor_signature_version VALUES (@id,@doctor,@version,CAST(@profile AS jsonb),@signature_png,@stamp_png,@hash,@actor,@time)",
            ("id", d.Id), ("doctor", d.DoctorId), ("version", d.Version), ("profile", Json(d)),
            ("signature_png", signature), ("stamp_png", stamp), ("hash", hash), ("actor", actor.Id), ("time", clock.GetUtcNow().UtcDateTime));
        await cmd.ExecuteNonQueryAsync(ct);
        await Audit(c, tx, actor, "doctor.version", d.Id, null, ct);
        await tx.CommitAsync(ct);
        return d;
    }
    public async Task SetDoctorActive(Guid doctorId, bool active, Actor actor, CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using var cmd = Command(c, tx, "UPDATE doctor SET active=@active WHERE id=@id", ("active", active), ("id", doctorId));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new NotFoundException("Doctor not found.");
        await Audit(c, tx, actor, "doctor.active", doctorId, null, ct);
        await tx.CommitAsync(ct);
    }
    public async Task<CenterSettings> Settings(CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT center_name,report_prefix,retention_days FROM settings WHERE singleton", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw new IntegrityException("Center settings are missing.");
        return new CenterSettings { CenterName = r.GetString(0), ReportPrefix = r.GetString(1), RetentionDays = r.GetInt32(2) };
    }
    public async Task SetSettings(CenterSettings settings, Actor actor, CancellationToken ct)
    {
        ReportValidator.Required(settings.CenterName, "Center name", 200);
        if (!System.Text.RegularExpressions.Regex.IsMatch(settings.ReportPrefix, "^[A-Z][A-Z0-9]{1,11}$") || settings.RetentionDays is < 30 or > 3650)
            throw new ValidationException("Invalid numbering prefix or retention days.");
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using var cmd = Command(c, tx, "UPDATE settings SET center_name=@name,report_prefix=@prefix,retention_days=@days WHERE singleton", ("name", settings.CenterName), ("prefix", settings.ReportPrefix), ("days", settings.RetentionDays));
        await cmd.ExecuteNonQueryAsync(ct);
        await Audit(c, tx, actor, "settings.update", Guid.Empty, null, ct);
        await tx.CommitAsync(ct);
    }
}
