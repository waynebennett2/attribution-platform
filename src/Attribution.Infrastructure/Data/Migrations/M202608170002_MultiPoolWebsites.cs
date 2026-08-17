using FluentMigrator;

namespace Attribution.Infrastructure.Data.Migrations;

// FR-050: per-website opt-in flag for multi-pool Dynamic Number Insertion, same pattern as
// shadow_mode_enabled (FR-049). Disabled by default so an existing website's behavior is
// unaffected until an administrator explicitly turns it on.
[Migration(202608170002)]
public class M202608170002_MultiPoolWebsites : Migration
{
    public override void Up()
    {
        Alter.Table("websites").AddColumn("multi_pool_enabled").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    public override void Down()
    {
        Delete.Column("multi_pool_enabled").FromTable("websites");
    }
}
