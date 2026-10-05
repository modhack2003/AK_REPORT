CREATE TABLE role (code text PRIMARY KEY);
INSERT INTO role VALUES ('Administrator'), ('Writer'), ('Receptionist'), ('MedicalReviewer');

CREATE TABLE app_user (
 id uuid PRIMARY KEY, username text NOT NULL UNIQUE, password_hash text NOT NULL,
 role_code text NOT NULL REFERENCES role(code), active boolean NOT NULL DEFAULT true,
 failed_attempts integer NOT NULL DEFAULT 0, locked_until timestamptz,
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE user_session (
 token_hash text PRIMARY KEY, user_id uuid NOT NULL REFERENCES app_user(id),
 created_at timestamptz NOT NULL, last_seen timestamptz NOT NULL,
 expires_at timestamptz NOT NULL, revoked boolean NOT NULL DEFAULT false
);
CREATE INDEX user_session_user ON user_session(user_id);

CREATE TABLE settings (
 singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
 center_name text NOT NULL, report_prefix text NOT NULL CHECK (report_prefix ~ '^[A-Z][A-Z0-9]{1,11}$'),
 retention_days integer NOT NULL CHECK (retention_days BETWEEN 30 AND 3650)
);
INSERT INTO settings VALUES (true, 'A K Diagnostic Centre & Polyclinic', 'AKDC', 365);
CREATE TABLE report_number_counter (
 prefix text NOT NULL, year integer NOT NULL, last_value bigint NOT NULL CHECK (last_value > 0),
 PRIMARY KEY(prefix, year)
);
CREATE TABLE patient (
 id uuid PRIMARY KEY, name text NOT NULL, local_id text NOT NULL,
 age integer, age_unit text NOT NULL, sex text NOT NULL
);
CREATE TABLE report_case (
 id uuid PRIMARY KEY, patient_id uuid NOT NULL REFERENCES patient(id),
 operation_id uuid NOT NULL UNIQUE, request_hash text NOT NULL,
 created_by uuid NOT NULL REFERENCES app_user(id), created_at timestamptz NOT NULL
);
CREATE TABLE report_type (code text PRIMARY KEY, title text NOT NULL);
CREATE TABLE report_template (
 id uuid PRIMARY KEY, report_type_code text NOT NULL UNIQUE REFERENCES report_type(code)
);
CREATE TABLE report_template_version (
 id uuid PRIMARY KEY, template_id uuid NOT NULL REFERENCES report_template(id),
 version integer NOT NULL CHECK (version > 0), definition jsonb NOT NULL,
 content_hash text NOT NULL, review_status integer NOT NULL CHECK (review_status IN (0,1)),
 review_evidence text NOT NULL, reviewed_by text NOT NULL, reviewed_at timestamptz,
 created_by uuid NOT NULL REFERENCES app_user(id), created_at timestamptz NOT NULL,
 UNIQUE(template_id, version), CHECK (review_status = 0 OR (length(review_evidence) > 0 AND length(reviewed_by) > 0 AND reviewed_at IS NOT NULL))
);
CREATE TABLE doctor (id uuid PRIMARY KEY, active boolean NOT NULL DEFAULT true);
CREATE TABLE doctor_signature_version (
 id uuid PRIMARY KEY, doctor_id uuid NOT NULL REFERENCES doctor(id),
 version integer NOT NULL CHECK (version > 0), profile jsonb NOT NULL,
 signature_png bytea, stamp_png bytea, content_hash text NOT NULL,
 created_by uuid NOT NULL REFERENCES app_user(id), created_at timestamptz NOT NULL,
 UNIQUE(doctor_id, version),
 CHECK (signature_png IS NULL OR octet_length(signature_png) <= 2097152),
 CHECK (stamp_png IS NULL OR octet_length(stamp_png) <= 2097152)
);
CREATE TABLE diagnostic_report (
 id uuid PRIMARY KEY, case_id uuid NOT NULL REFERENCES report_case(id),
 report_type_code text NOT NULL REFERENCES report_type(code), public_number text NOT NULL UNIQUE,
 operation_id uuid NOT NULL UNIQUE, request_hash text NOT NULL,
 current_revision_id uuid, retention_state integer NOT NULL DEFAULT 0 CHECK (retention_state IN (0,1,2)),
 created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL
);
CREATE INDEX diagnostic_report_case ON diagnostic_report(case_id);
CREATE TABLE report_revision (
 id uuid PRIMARY KEY, report_id uuid NOT NULL REFERENCES diagnostic_report(id),
 revision_number integer NOT NULL CHECK (revision_number > 0), previous_revision_id uuid,
 template_version_id uuid NOT NULL REFERENCES report_template_version(id),
 doctor_version_id uuid REFERENCES doctor_signature_version(id), metadata jsonb NOT NULL, formatting jsonb NOT NULL,
 state integer NOT NULL CHECK (state IN (0,1)), reason text NOT NULL CHECK (length(reason) > 0),
 authorization_basis text NOT NULL, changed_paths jsonb NOT NULL,
 created_by uuid NOT NULL REFERENCES app_user(id), created_at timestamptz NOT NULL,
 UNIQUE(report_id, revision_number), UNIQUE(report_id, id),
 FOREIGN KEY(report_id, previous_revision_id) REFERENCES report_revision(report_id, id),
 CHECK ((revision_number = 1 AND previous_revision_id IS NULL) OR (revision_number > 1 AND previous_revision_id IS NOT NULL)),
 CHECK (state = 0 OR (doctor_version_id IS NOT NULL AND length(authorization_basis) > 0))
);
ALTER TABLE diagnostic_report ADD CONSTRAINT current_revision_same_report
 FOREIGN KEY(id, current_revision_id) REFERENCES report_revision(report_id, id) DEFERRABLE INITIALLY DEFERRED;
CREATE TABLE report_section (
 id uuid PRIMARY KEY, revision_id uuid NOT NULL REFERENCES report_revision(id),
 code text NOT NULL, visible boolean NOT NULL, UNIQUE(revision_id, code)
);
CREATE TABLE report_result (
 section_id uuid NOT NULL REFERENCES report_section(id), field_code text NOT NULL,
 row_index integer NOT NULL CHECK (row_index BETWEEN 0 AND 999), kind integer NOT NULL CHECK (kind BETWEEN 0 AND 5),
 numeric_value numeric, text_value text NOT NULL, comparator text NOT NULL,
 unit text NOT NULL, reference_text text NOT NULL,
 PRIMARY KEY(section_id, field_code, row_index),
 CHECK ((kind = 0 AND numeric_value IS NOT NULL AND text_value = '' AND comparator IN ('','<','<=','>','>='))
 OR (kind <> 0 AND numeric_value IS NULL AND length(text_value) > 0 AND comparator = ''))
);
CREATE TABLE generated_document (
 id uuid PRIMARY KEY, revision_id uuid NOT NULL REFERENCES report_revision(id),
 format text NOT NULL CHECK (format IN ('pdf','docx')), bytes bytea NOT NULL,
 sha256 text NOT NULL, page_count integer NOT NULL CHECK (page_count > 0), engine_version text NOT NULL,
 page_plan jsonb NOT NULL, created_by uuid NOT NULL REFERENCES app_user(id), created_at timestamptz NOT NULL,
 UNIQUE(revision_id, format, engine_version, sha256), CHECK (octet_length(bytes) BETWEEN 1 AND 52428800)
);
CREATE TABLE audit_event (
 id uuid PRIMARY KEY, actor_id uuid REFERENCES app_user(id), action text NOT NULL,
 entity_id uuid NOT NULL, revision_number integer, created_at timestamptz NOT NULL
);
CREATE INDEX audit_event_entity ON audit_event(entity_id, created_at);

CREATE FUNCTION reject_historical_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Historical records are append-only' USING ERRCODE='55000'; END $$;
DO $$ DECLARE name text; BEGIN
 FOREACH name IN ARRAY ARRAY['patient','report_case','report_template_version','doctor_signature_version',
 'report_revision','report_section','report_result','generated_document','audit_event'] LOOP
 EXECUTE format('CREATE TRIGGER immutable_row BEFORE UPDATE OR DELETE ON %I FOR EACH ROW EXECUTE FUNCTION reject_historical_mutation()', name);
 END LOOP;
END $$;
