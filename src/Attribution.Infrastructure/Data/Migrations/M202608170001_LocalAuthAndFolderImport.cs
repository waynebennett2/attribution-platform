using FluentMigrator;

namespace Attribution.Infrastructure.Data.Migrations;

// FR-046: federation dropped in favor of local username/password + TOTP MFA as the
// platform's sole interactive sign-in method — `subject_ref` (the IdP subject) is no
// longer meaningful, and a rotating refresh token replaces the federated silent-refresh
// mechanism. Any existing `identity_type` value from before this change is normalised to
// `Local` so no row is left referencing a removed identity type.
[Migration(202608170001)]
public class M202608170001_LocalAuthAndFolderImport : Migration
{
    public override void Up()
    {
        Execute.Sql("UPDATE users SET identity_type = 'Local' WHERE identity_type IN ('Federated', 'BreakGlass')");

        Delete.Index("IX_users_subject_ref").OnTable("users");
        Delete.Column("subject_ref").FromTable("users");

        Alter.Table("users").AddColumn("refresh_token_hash").AsString(64).Nullable();
        Alter.Table("users").AddColumn("refresh_token_expires_at").AsCustom("DATETIME(6)").Nullable();
        Create.Index("IX_users_refresh_token_hash").OnTable("users").OnColumn("refresh_token_hash").Ascending();
    }

    public override void Down()
    {
        Delete.Index("IX_users_refresh_token_hash").OnTable("users");
        Delete.Column("refresh_token_hash").FromTable("users");
        Delete.Column("refresh_token_expires_at").FromTable("users");

        Alter.Table("users").AddColumn("subject_ref").AsString(255).Nullable();
        Create.Index("IX_users_subject_ref").OnTable("users").OnColumn("subject_ref").Ascending().WithOptions().Unique();
    }
}
