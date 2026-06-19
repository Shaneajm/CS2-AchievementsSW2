namespace AchievementsSW2.Plugin.Models;

public sealed class PlayerAchievement
{
	public string AchievementId { get; set; } = string.Empty;

	public int Progress { get; set; }

	public bool IsCompleted { get; set; }

	public DateTime? CompletedAt { get; set; }

	public static PlayerAchievement FromDb(DbAchievementProgress row) => new()
	{
		AchievementId = row.AchievementId,
		Progress = row.Progress,
		IsCompleted = row.Completed,
		CompletedAt = row.CompletedAt
	};
}
