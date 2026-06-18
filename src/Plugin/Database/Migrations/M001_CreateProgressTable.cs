using FluentMigrator;

namespace AchievementsSW2.Plugin.Database.Migrations;

[Migration(202606181445)]
public sealed class M001_CreateProgressTable : Migration
{
	public override void Up()
	{
		if (Schema.Table("achievementssw2_progress").Exists())
			return;

		Create.Table("achievementssw2_progress")
			.WithColumn("id").AsInt32().NotNullable().PrimaryKey().Identity()
			.WithColumn("steamid64").AsInt64().NotNullable()
			.WithColumn("achievement_id").AsString(128).NotNullable()
			.WithColumn("season_key").AsString(128).NotNullable().WithDefaultValue(string.Empty)
			.WithColumn("progress").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("completed").AsBoolean().NotNullable().WithDefaultValue(false)
			.WithColumn("completed_at").AsDateTime().Nullable()
			.WithColumn("updated_at").AsDateTime().NotNullable();

		Create.Index("idx_achievementssw2_progress_player")
			.OnTable("achievementssw2_progress")
			.OnColumn("steamid64");

		Create.Index("idx_achievementssw2_progress_achievement")
			.OnTable("achievementssw2_progress")
			.OnColumn("achievement_id");

		Create.Index("idx_achievementssw2_progress_unique")
			.OnTable("achievementssw2_progress")
			.OnColumn("steamid64").Ascending()
			.OnColumn("achievement_id").Ascending()
			.OnColumn("season_key").Ascending()
			.WithOptions().Unique();
	}

	public override void Down()
	{
		if (!Schema.Table("achievementssw2_progress").Exists())
			return;

		Delete.Table("achievementssw2_progress");
	}
}
