-- Run with psql as the migration owner, supplying -v runtime_role=ak_reporting_app.
-- Create the LOGIN role/password separately using your protected installation process.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO :"runtime_role";
GRANT SELECT ON ALL TABLES IN SCHEMA public TO :"runtime_role";
GRANT INSERT ON app_user,user_session,report_number_counter,patient,report_case,report_type,report_template,
 report_template_version,doctor,doctor_signature_version,diagnostic_report,report_revision,
 report_section,report_result,generated_document,audit_event TO :"runtime_role";
GRANT UPDATE (failed_attempts,locked_until,password_hash,active) ON app_user TO :"runtime_role";
GRANT UPDATE (last_seen,revoked) ON user_session TO :"runtime_role";
GRANT UPDATE (last_value) ON report_number_counter TO :"runtime_role";
GRANT UPDATE (current_revision_id,retention_state) ON diagnostic_report TO :"runtime_role";
GRANT UPDATE (active) ON doctor TO :"runtime_role";
GRANT UPDATE (center_name,report_prefix,retention_days) ON settings TO :"runtime_role";
