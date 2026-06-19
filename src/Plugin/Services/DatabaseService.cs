using AchievementsSW2.Plugin.Config;
using AchievementsSW2.Plugin.Database.Migrations;
using AchievementsSW2.Plugin.Models;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

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

			var parameters = new
			{
				SteamId64 = (long)steamId,
				AchievementId = achievementId,
				SeasonKey = normalizedSeasonKey,
				Progress = Math.Max(0, progress),
				Completed = completed,
				CompletedAt = completed ? completedAt ?? now : (DateTime?)null,
				UpdatedAt = now
			};

			await connection.ExecuteAsync(GetUpsertSql(connection), parameters);

			return await connection.QueryFirstOrDefaultAsync<DbAchievementProgress>(
				$"""
				SELECT * FROM {TableName}
				WHERE steamid64 = @SteamId64
					AND achievement_id = @AchievementId
					AND season_key = @SeasonKey
				""",
				parameters);
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

	private static string GetUpsertSql(System.Data.IDbConnection connection)
	{
		return connection switch
		{
			MySqlConnection => $"""
				INSERT INTO {TableName}
					(steamid64, achievement_id, season_key, progress, completed, completed_at, updated_at)
				VALUES
					(@SteamId64, @AchievementId, @SeasonKey, @Progress, @Completed, @CompletedAt, @UpdatedAt)
				ON DUPLICATE KEY UPDATE
					progress = GREATEST(progress, VALUES(progress)),
					completed = completed OR VALUES(completed),
					completed_at = CASE
						WHEN completed_at IS NOT NULL THEN completed_at
						WHEN VALUES(completed) THEN VALUES(completed_at)
						ELSE NULL
					END,
					updated_at = VALUES(updated_at)
				""",

			NpgsqlConnection => $"""
				INSERT INTO {TableName}
					(steamid64, achievement_id, season_key, progress, completed, completed_at, updated_at)
				VALUES
					(@SteamId64, @AchievementId, @SeasonKey, @Progress, @Completed, @CompletedAt, @UpdatedAt)
				ON CONFLICT (steamid64, achievement_id, season_key) DO UPDATE
				SET
					progress = GREATEST({TableName}.progress, EXCLUDED.progress),
					completed = {TableName}.completed OR EXCLUDED.completed,
					completed_at = CASE
						WHEN {TableName}.completed_at IS NOT NULL THEN {TableName}.completed_at
						WHEN EXCLUDED.completed THEN EXCLUDED.completed_at
						ELSE NULL
					END,
					updated_at = EXCLUDED.updated_at
				""",

			SqliteConnection => $"""
				INSERT INTO {TableName}
					(steamid64, achievement_id, season_key, progress, completed, completed_at, updated_at)
				VALUES
					(@SteamId64, @AchievementId, @SeasonKey, @Progress, @Completed, @CompletedAt, @UpdatedAt)
				ON CONFLICT (steamid64, achievement_id, season_key) DO UPDATE
				SET
					progress = MAX(progress, excluded.progress),
					completed = completed OR excluded.completed,
					completed_at = CASE
						WHEN completed_at IS NOT NULL THEN completed_at
						WHEN excluded.completed THEN excluded.completed_at
						ELSE NULL
					END,
					updated_at = excluded.updated_at
				""",

			_ => throw new NotSupportedException(
				$"Unsupported database connection type: {connection.GetType().Name}")
		};
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
