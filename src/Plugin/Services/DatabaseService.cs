using AchievementsSW2.Plugin.Config;
using AchievementsSW2.Plugin.Database.Migrations;
using AchievementsSW2.Plugin.Models;
using Dapper;
using Dommel;
using Microsoft.Extensions.Logging;

namespace AchievementsSW2.Plugin.Services;

public sealed class DatabaseService(string connectionName)
{
	internal const string TableName = "achievementssw2_progress";

	private readonly string _connectionName = connectionName;

	public bool IsEnabled { get; private set; }

	public async Task InitializeAsync()
	{
		try
		{
			using var connection = AchievementsSW2.Core.Database.GetConnection(_connectionName);
			MigrationRunner.RunMigrations(connection);

			IsEnabled = true;
			AchievementsSW2.Core.Logger.LogInformation("Database initialized. Table: {Table}", TableName);

			await Task.CompletedTask;
		}
		catch (Exception ex)
		{
			IsEnabled = false;
			AchievementsSW2.Core.Logger.LogError(ex, "Failed to initialize database. Achievement progress will not persist.");
		}
	}

	public async Task<List<DbAchievementProgress>> GetPlayerProgressAsync(ulong steamId, string? seasonKey)
	{
		if (!IsEnabled)
			return [];

		try
		{
			using var connection = AchievementsSW2.Core.Database.GetConnection(_connectionName);
			connection.Open();

			var rows = await connection.QueryAsync<DbAchievementProgress>(
				$"SELECT * FROM {TableName} WHERE steamid64 = @SteamId64 AND season_key = @SeasonKey",
				new
				{
					SteamId64 = (long)steamId,
					SeasonKey = NormalizeSeasonKey(seasonKey)
				});

			return rows.ToList();
		}
		catch (Exception ex)
		{
			AchievementsSW2.Core.Logger.LogError(ex, "Failed to load achievement progress for {SteamId}", steamId);
			return [];
		}
	}

	public async Task<DbAchievementProgress?> GetProgressAsync(ulong steamId, string achievementId, string? seasonKey)
	{
		if (!IsEnabled)
			return null;

		try
		{
			using var connection = AchievementsSW2.Core.Database.GetConnection(_connectionName);
			connection.Open();

			return await connection.QueryFirstOrDefaultAsync<DbAchievementProgress>(
				$"""
				SELECT * FROM {TableName}
				WHERE steamid64 = @SteamId64
					AND achievement_id = @AchievementId
					AND season_key = @SeasonKey
				""",
				new
				{
					SteamId64 = (long)steamId,
					AchievementId = achievementId,
					SeasonKey = NormalizeSeasonKey(seasonKey)
				});
		}
		catch (Exception ex)
		{
			AchievementsSW2.Core.Logger.LogError(
				ex,
				"Failed to load progress for achievement {AchievementId} and player {SteamId}",
				achievementId,
				steamId);
			return null;
		}
	}

	public async Task<DbAchievementProgress?> SaveProgressAsync(
		ulong steamId,
		string achievementId,
		string? seasonKey,
		int progress,
		bool completed,
		DateTime? completedAt)
	{
		if (!IsEnabled)
			return null;

		var normalizedSeasonKey = NormalizeSeasonKey(seasonKey);
		var now = DateTime.UtcNow;

		try
		{
			using var connection = AchievementsSW2.Core.Database.GetConnection(_connectionName);
			connection.Open();

			var existing = await connection.QueryFirstOrDefaultAsync<DbAchievementProgress>(
				$"""
				SELECT * FROM {TableName}
				WHERE steamid64 = @SteamId64
					AND achievement_id = @AchievementId
					AND season_key = @SeasonKey
				""",
				new
				{
					SteamId64 = (long)steamId,
					AchievementId = achievementId,
					SeasonKey = normalizedSeasonKey
				});

			if (existing == null)
			{
				var row = new DbAchievementProgress
				{
					SteamId64 = (long)steamId,
					AchievementId = achievementId,
					SeasonKey = normalizedSeasonKey,
					Progress = Math.Max(0, progress),
					Completed = completed,
					CompletedAt = completed ? completedAt ?? now : null,
					UpdatedAt = now
				};

				var id = await connection.InsertAsync(row);
				row.Id = Convert.ToInt32(id);
				return row;
			}

			existing.Progress = Math.Max(0, progress);
			existing.Completed = existing.Completed || completed;
			existing.CompletedAt = existing.Completed
				? existing.CompletedAt ?? completedAt ?? now
				: null;
			existing.UpdatedAt = now;

			await connection.ExecuteAsync(
				$"""
				UPDATE {TableName}
				SET progress = @Progress,
					completed = @Completed,
					completed_at = @CompletedAt,
					updated_at = @UpdatedAt
				WHERE id = @Id
				""",
				existing);

			return existing;
		}
		catch (Exception ex)
		{
			AchievementsSW2.Core.Logger.LogError(
				ex,
				"Failed to save progress for achievement {AchievementId} and player {SteamId}",
				achievementId,
				steamId);
			return null;
		}
	}

	public Task<DbAchievementProgress?> CompleteAchievementAsync(
		ulong steamId,
		string achievementId,
		string? seasonKey,
		int requiredAmount)
	{
		return SaveProgressAsync(
			steamId,
			achievementId,
			seasonKey,
			requiredAmount,
			completed: true,
			completedAt: DateTime.UtcNow);
	}

	public static string NormalizeSeasonKey(string? seasonKey)
	{
		return string.IsNullOrWhiteSpace(seasonKey) ? string.Empty : seasonKey.Trim();
	}
}
