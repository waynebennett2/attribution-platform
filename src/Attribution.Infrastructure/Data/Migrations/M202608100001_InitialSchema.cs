using FluentMigrator;

namespace Attribution.Infrastructure.Data.Migrations;

// Baseline schema covering every entity in data-model.md (T010). GUIDs are stored as
// CHAR(36) for portability; timestamps are DATETIME(6) for sub-second precision (needed
// for allocation-window overlap checks). Role is a fixed 4-value enum per FR-032, not an
// administrator-configurable set, so it is stored as a string column on `users` rather
// than as a separate lookup table.
[Migration(202608100001)]
public class M202608100001_InitialSchema : Migration
{
    public override void Up()
    {
        Create.Table("websites")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("permitted_origins").AsString(int.MaxValue).NotNullable()
            .WithColumn("default_number").AsString(32).NotNullable()
            .WithColumn("session_timeout_seconds").AsInt32().NotNullable().WithDefaultValue(1800)
            .WithColumn("heartbeat_interval_seconds").AsInt32().NotNullable().WithDefaultValue(300)
            .WithColumn("allocation_window_extension_seconds").AsInt32().NotNullable().WithDefaultValue(1800)
            .WithColumn("cooldown_seconds").AsInt32().NotNullable().WithDefaultValue(1800)
            .WithColumn("consent_required").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("shadow_mode_enabled").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("business_unit").AsString(255).Nullable()
            .WithColumn("local_timezone").AsString(64).NotNullable().WithDefaultValue("UTC")
            .WithColumn("created_at").AsDateTime2().NotNullable()
            .WithColumn("updated_at").AsDateTime2().NotNullable();

        Create.Table("number_pools")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("scope_type").AsString(32).NotNullable()
            .WithColumn("scope_ref").AsString(36).NotNullable()
            .WithColumn("default_number").AsString(32).Nullable()
            .WithColumn("created_at").AsDateTime2().NotNullable()
            .WithColumn("updated_at").AsDateTime2().NotNullable();

        Create.Table("tracking_numbers")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("pool_id").AsString(36).NotNullable()
            .WithColumn("did").AsString(32).NotNullable().Unique()
            .WithColumn("status").AsString(16).NotNullable()
            .WithColumn("status_changed_at").AsDateTime2().NotNullable()
            .WithColumn("last_released_at").AsDateTime2().Nullable();
        Create.ForeignKey("FK_tracking_numbers_pool").FromTable("tracking_numbers").ForeignColumn("pool_id")
            .ToTable("number_pools").PrimaryColumn("id");
        Create.Index("IX_tracking_numbers_pool_status").OnTable("tracking_numbers")
            .OnColumn("pool_id").Ascending().OnColumn("status").Ascending();

        Create.Table("visitors")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("website_id").AsString(36).NotNullable()
            .WithColumn("first_seen_at").AsDateTime2().NotNullable()
            .WithColumn("de_identified_at").AsDateTime2().Nullable();
        Create.ForeignKey("FK_visitors_website").FromTable("visitors").ForeignColumn("website_id")
            .ToTable("websites").PrimaryColumn("id");

        Create.Table("sessions")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("visitor_id").AsString(36).NotNullable()
            .WithColumn("website_id").AsString(36).NotNullable()
            .WithColumn("landing_page").AsString(2048).Nullable()
            .WithColumn("referrer").AsString(2048).Nullable()
            .WithColumn("utm_source").AsString(255).Nullable()
            .WithColumn("utm_medium").AsString(255).Nullable()
            .WithColumn("utm_campaign").AsString(255).Nullable()
            .WithColumn("utm_term").AsString(255).Nullable()
            .WithColumn("utm_content").AsString(255).Nullable()
            .WithColumn("gclid").AsString(255).Nullable()
            .WithColumn("gbraid").AsString(255).Nullable()
            .WithColumn("wbraid").AsString(255).Nullable()
            .WithColumn("ga4_client_id").AsString(255).Nullable()
            .WithColumn("consent_state").AsString(16).NotNullable()
            .WithColumn("provenance").AsString(16).NotNullable().WithDefaultValue("ordinary")
            .WithColumn("started_at").AsDateTime2().NotNullable()
            .WithColumn("expires_at").AsDateTime2().NotNullable()
            .WithColumn("ended_at").AsDateTime2().Nullable();
        Create.ForeignKey("FK_sessions_visitor").FromTable("sessions").ForeignColumn("visitor_id")
            .ToTable("visitors").PrimaryColumn("id");
        Create.ForeignKey("FK_sessions_website").FromTable("sessions").ForeignColumn("website_id")
            .ToTable("websites").PrimaryColumn("id");

        Create.Table("allocations")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("tracking_number_id").AsString(36).NotNullable()
            .WithColumn("session_id").AsString(36).NotNullable()
            .WithColumn("pool_id_at_allocation").AsString(36).Nullable()
            .WithColumn("window_start").AsDateTime2().NotNullable()
            .WithColumn("window_end").AsDateTime2().NotNullable()
            .WithColumn("is_shadow").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("created_at").AsDateTime2().NotNullable();
        Create.ForeignKey("FK_allocations_number").FromTable("allocations").ForeignColumn("tracking_number_id")
            .ToTable("tracking_numbers").PrimaryColumn("id");
        Create.ForeignKey("FK_allocations_session").FromTable("allocations").ForeignColumn("session_id")
            .ToTable("sessions").PrimaryColumn("id");
        // FR-003/FR-006: no two allocations for the same number may have overlapping windows
        // in ordinary operation; the allocation query (research.md §2) enforces this at write
        // time via FOR UPDATE SKIP LOCKED, this index just makes that lookup efficient.
        Create.Index("IX_allocations_number_window").OnTable("allocations")
            .OnColumn("tracking_number_id").Ascending().OnColumn("window_start").Ascending();

        Create.Table("calls")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("source_record_id").AsString(128).NotNullable().Unique()
            .WithColumn("direction").AsString(16).NotNullable()
            .WithColumn("dialled_number").AsString(32).NotNullable()
            .WithColumn("caller_id").AsString(64).Nullable()
            .WithColumn("started_at").AsDateTime2().NotNullable()
            .WithColumn("answered_at").AsDateTime2().Nullable()
            .WithColumn("ended_at").AsDateTime2().Nullable()
            .WithColumn("connected_duration_seconds").AsInt32().Nullable()
            .WithColumn("disposition").AsString(32).Nullable()
            .WithColumn("is_final").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("ingested_at").AsDateTime2().NotNullable()
            .WithColumn("updated_at").AsDateTime2().NotNullable();
        Create.Index("IX_calls_dialled_number_started_at").OnTable("calls")
            .OnColumn("dialled_number").Ascending().OnColumn("started_at").Ascending();

        Create.Table("call_legs")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("call_id").AsString(36).Nullable() // nullable: a leg may arrive before its parent CDR
            .WithColumn("source_call_record_id").AsString(128).NotNullable()
            .WithColumn("source_leg_id").AsString(128).NotNullable()
            .WithColumn("sequence_or_role").AsString(64).Nullable()
            .WithColumn("started_at").AsDateTime2().Nullable()
            .WithColumn("ended_at").AsDateTime2().Nullable();
        Create.ForeignKey("FK_call_legs_call").FromTable("call_legs").ForeignColumn("call_id")
            .ToTable("calls").PrimaryColumn("id");
        Create.Index("IX_call_legs_source").OnTable("call_legs")
            .OnColumn("source_call_record_id").Ascending().OnColumn("source_leg_id").Ascending().WithOptions().Unique();

        Create.Table("attributions")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("call_id").AsString(36).NotNullable()
            .WithColumn("session_id").AsString(36).Nullable()
            .WithColumn("allocation_id").AsString(36).Nullable()
            .WithColumn("state").AsString(16).NotNullable()
            .WithColumn("reason").AsString(255).Nullable()
            .WithColumn("is_shadow_derived").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("is_current").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("superseded_reason").AsString(255).Nullable()
            .WithColumn("decided_at").AsDateTime2().NotNullable();
        Create.ForeignKey("FK_attributions_call").FromTable("attributions").ForeignColumn("call_id")
            .ToTable("calls").PrimaryColumn("id");
        Create.ForeignKey("FK_attributions_session").FromTable("attributions").ForeignColumn("session_id")
            .ToTable("sessions").PrimaryColumn("id");
        Create.ForeignKey("FK_attributions_allocation").FromTable("attributions").ForeignColumn("allocation_id")
            .ToTable("allocations").PrimaryColumn("id");
        Create.Index("IX_attributions_call_current").OnTable("attributions")
            .OnColumn("call_id").Ascending().OnColumn("is_current").Ascending();

        Create.Table("qualification_rules")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("scope_type").AsString(16).NotNullable()
            .WithColumn("scope_ref").AsString(36).Nullable()
            .WithColumn("version").AsInt32().NotNullable()
            .WithColumn("conditions").AsString(int.MaxValue).NotNullable()
            .WithColumn("effective_start").AsDateTime2().NotNullable()
            .WithColumn("effective_end").AsDateTime2().Nullable()
            .WithColumn("created_by").AsString(36).NotNullable()
            .WithColumn("created_at").AsDateTime2().NotNullable();
        Create.Index("IX_qualification_rules_scope_version").OnTable("qualification_rules")
            .OnColumn("scope_type").Ascending().OnColumn("scope_ref").Ascending().OnColumn("version").Ascending().WithOptions().Unique();

        Create.Table("qualification_results")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("call_id").AsString(36).NotNullable()
            .WithColumn("attribution_id").AsString(36).NotNullable()
            .WithColumn("qualification_rule_id").AsString(36).NotNullable()
            .WithColumn("is_qualified").AsBoolean().NotNullable()
            .WithColumn("is_current").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("superseded_reason").AsString(255).Nullable()
            .WithColumn("decided_at").AsDateTime2().NotNullable();
        Create.ForeignKey("FK_qualification_results_call").FromTable("qualification_results").ForeignColumn("call_id")
            .ToTable("calls").PrimaryColumn("id");
        Create.ForeignKey("FK_qualification_results_attribution").FromTable("qualification_results").ForeignColumn("attribution_id")
            .ToTable("attributions").PrimaryColumn("id");
        Create.ForeignKey("FK_qualification_results_rule").FromTable("qualification_results").ForeignColumn("qualification_rule_id")
            .ToTable("qualification_rules").PrimaryColumn("id");
        Create.Index("IX_qualification_results_call_current").OnTable("qualification_results")
            .OnColumn("call_id").Ascending().OnColumn("is_current").Ascending();

        Create.Table("conversion_publications")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("qualification_result_id").AsString(36).NotNullable()
            .WithColumn("destination").AsString(16).NotNullable()
            .WithColumn("idempotency_key").AsString(255).NotNullable().Unique()
            .WithColumn("status").AsString(16).NotNullable()
            .WithColumn("skipped_reason").AsString(255).Nullable()
            .WithColumn("attempt_count").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("external_id").AsString(255).Nullable()
            .WithColumn("last_error").AsString(int.MaxValue).Nullable()
            .WithColumn("correction").AsString(int.MaxValue).Nullable()
            .WithColumn("sent_at").AsDateTime2().Nullable()
            .WithColumn("corrected_at").AsDateTime2().Nullable();
        Create.ForeignKey("FK_conversion_publications_result").FromTable("conversion_publications").ForeignColumn("qualification_result_id")
            .ToTable("qualification_results").PrimaryColumn("id");

        Create.Table("ingestion_checkpoints")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("feed").AsString(32).NotNullable().Unique()
            .WithColumn("position").AsString(255).NotNullable()
            .WithColumn("updated_at").AsDateTime2().NotNullable();

        Create.Table("users")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("subject_ref").AsString(255).Nullable()
            .WithColumn("username").AsString(255).Nullable()
            .WithColumn("client_id").AsString(255).Nullable()
            .WithColumn("identity_type").AsString(24).NotNullable()
            .WithColumn("mapped_role").AsString(32).NotNullable()
            .WithColumn("role_override").AsString(32).Nullable()
            .WithColumn("role_overridden_by").AsString(36).Nullable()
            .WithColumn("mfa_required").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("created_at").AsDateTime2().NotNullable()
            .WithColumn("last_seen_at").AsDateTime2().Nullable();
        Create.Index("IX_users_subject_ref").OnTable("users").OnColumn("subject_ref").Ascending().WithOptions().Unique();

        Create.Table("alerts")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("condition_type").AsString(32).NotNullable()
            .WithColumn("scope_ref").AsString(255).Nullable()
            .WithColumn("threshold").AsString(64).NotNullable()
            .WithColumn("raised_at").AsDateTime2().NotNullable()
            .WithColumn("last_notified_at").AsDateTime2().NotNullable()
            .WithColumn("acknowledged_at").AsDateTime2().Nullable()
            .WithColumn("acknowledged_by").AsString(36).Nullable()
            .WithColumn("cleared_at").AsDateTime2().Nullable();
        // FR-047: one open alert per (condition_type, scope_ref) — enforced at the application
        // layer (the AlertingService looks up an existing open row before inserting), since
        // MySQL has no portable partial/filtered unique index for "WHERE cleared_at IS NULL".
        Create.Index("IX_alerts_condition_scope_open").OnTable("alerts")
            .OnColumn("condition_type").Ascending().OnColumn("scope_ref").Ascending().OnColumn("cleared_at").Ascending();

        // FR-035: append-only by convention here; the deployment's database user grants
        // (INSERT + SELECT only on this table, no UPDATE/DELETE) are an ops/deployment
        // concern applied outside migrations, which only define shape.
        Create.Table("audit_entries")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("actor_user_id").AsString(36).NotNullable()
            .WithColumn("action").AsString(128).NotNullable()
            .WithColumn("target_type").AsString(64).NotNullable()
            .WithColumn("target_id").AsString(64).NotNullable()
            .WithColumn("before_value").AsString(int.MaxValue).Nullable()
            .WithColumn("after_value").AsString(int.MaxValue).NotNullable()
            .WithColumn("occurred_at").AsDateTime2().NotNullable();
        Create.Index("IX_audit_entries_target").OnTable("audit_entries")
            .OnColumn("target_type").Ascending().OnColumn("target_id").Ascending();

        Create.Table("review_cases")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("call_id").AsString(36).NotNullable()
            .WithColumn("attribution_id").AsString(36).NotNullable()
            .WithColumn("status").AsString(16).NotNullable()
            .WithColumn("opened_at").AsDateTime2().NotNullable()
            .WithColumn("age_alert_raised_at").AsDateTime2().Nullable()
            .WithColumn("resolved_by").AsString(36).Nullable()
            .WithColumn("resolved_at").AsDateTime2().Nullable()
            .WithColumn("resolution").AsString(int.MaxValue).Nullable();
        Create.ForeignKey("FK_review_cases_call").FromTable("review_cases").ForeignColumn("call_id")
            .ToTable("calls").PrimaryColumn("id");
        Create.ForeignKey("FK_review_cases_attribution").FromTable("review_cases").ForeignColumn("attribution_id")
            .ToTable("attributions").PrimaryColumn("id");
        Create.Index("IX_review_cases_status_opened").OnTable("review_cases")
            .OnColumn("status").Ascending().OnColumn("opened_at").Ascending();
    }

    public override void Down()
    {
        Delete.Table("review_cases");
        Delete.Table("audit_entries");
        Delete.Table("alerts");
        Delete.Table("users");
        Delete.Table("ingestion_checkpoints");
        Delete.Table("conversion_publications");
        Delete.Table("qualification_results");
        Delete.Table("qualification_rules");
        Delete.Table("attributions");
        Delete.Table("call_legs");
        Delete.Table("calls");
        Delete.Table("allocations");
        Delete.Table("sessions");
        Delete.Table("visitors");
        Delete.Table("tracking_numbers");
        Delete.Table("number_pools");
        Delete.Table("websites");
    }
}
