using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AchievementsSW2.Plugin.Models;

[Table("achievementssw2_progress")]
public sealed class DbAchievementProgress
{
	[Key]
	[Column("id")]
	public int Id { get; set; }

	[Column("steamid64")]
	public long SteamId64 { get; set; }

	[Column("achievement_id")]
	public string AchievementId { get; set; } = string.Empty;

	[Column("season_key")]
	public string SeasonKey { get; set; } = string.Empty;

	[Column("progress")]
	public int Progress { get; set; }

	[Column("completed")]
	public bool Completed { get; set; }

	[Column("completed_at")]
	public DateTime? CompletedAt { get; set; }

	[Column("updated_at")]
	public DateTime UpdatedAt { get; set; }
}
