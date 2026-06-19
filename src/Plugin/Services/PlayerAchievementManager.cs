using System.Collections.Concurrent;
using System.Text.Json;
using AchievementsSW2.Plugin.Config;
using AchievementsSW2.Plugin.Models;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;

namespace AchievementsSW2.Plugin.Services;

public sealed class PlayerAchievementManager(
	DatabaseService database,
	AchievementLoader achievementLoader,
	Func<PluginConfig> getConfig)
{
	private readonly ConcurrentDictionary<ulong, AchievementPlayer> _players = new();
	private readonly DatabaseService _database = database;
	private readonly AchievementLoader _achievementLoader = achievementLoader;
	private readonly Func<PluginConfig> _getConfig = getConfig;
	private volatile int _activePlayerCount;

	public IEnumerable<AchievementPlayer> AllPlayers => _players.Values;

	public int ActivePlayerCount => _activePlayerCount;

	public AchievementPlayer GetOrCreatePlayer(IPlayer player)
	{
		var created = false;
		var achievementPlayer = _players.GetOrAdd(player.SteamID, _ =>
		{
			created = true;
			return new AchievementPlayer
			{
				SteamId = player.SteamID,
				Player = player
			};
		});

		if (created)
		{
			RefreshActivePlayerCount();

			_ = Task.Run(async () =>
			{
				try { await LoadPlayerDataAsync(achievementPlayer); }
				catch (Exception ex)
				{
					AchievementsSW2.Core.Logger.LogError(
						ex,
						"Failed to load achievements for {SteamId}",
						achievementPlayer.SteamId);
				}
			});
		}

		return achievementPlayer;
	}

	public AchievementPlayer? GetPlayer(IPlayer player)
	{
		return _players.TryGetValue(player.SteamID, out var achievementPlayer)
			? achievementPlayer
			: null;
	}

	public IReadOnlyDictionary<string, PlayerAchievement> GetProgressSnapshot(AchievementPlayer player)
	{
		return Snapshot(player).ToDictionary(
			progress => progress.AchievementId,
			progress => progress,
			StringComparer.OrdinalIgnoreCase);
	}

	public void RemovePlayer(ulong steamId)
	{
		if (!_players.TryRemove(steamId, out var player))
			return;

		RefreshActivePlayerCount();

		if (!player.IsLoaded)
			return;

		var snapshot = Snapshot(player);
		_ = Task.Run(async () =>
		{
			try { await SaveProgressSnapshotAsync(player.SteamId, snapshot); }
			catch (Exception ex)
			{
				AchievementsSW2.Core.Logger.LogError(ex, "Failed to save achievements on disconnect for {SteamId}", steamId);
			}
		});
	}

	public void ProcessEvent(
		string eventType,
		string target,
		IPlayer player,
		Dictionary<string, object?>? eventProperties = null,
		string? mapName = null)
	{
		var config = _getConfig();
		if (ActivePlayerCount < config.MinimumPlayers)
			return;

		var achievementPlayer = GetPlayer(player);
		if (achievementPlayer is not { IsLoaded: true, IsValid: true })
			return;

		var matchingAchievements = _achievementLoader
			.GetActiveAchievements(config.ServerType, mapName)
			.Where(achievement => MatchesAchievement(achievement, eventType, target, eventProperties))
			.Where(achievement => string.IsNullOrWhiteSpace(achievement.Flag) ||
				AchievementsSW2.Core.Permission.PlayerHasPermission(player.SteamID, achievement.Flag))
			.ToList();

		foreach (var achievement in matchingAchievements)
		{
			AddProgress(achievementPlayer, achievement, 1);
		}
	}

	public void AddProgress(AchievementPlayer player, AchievementDefinition achievement, int amount)
	{
		if (amount <= 0 || !player.IsLoaded || !player.IsValid)
			return;

		PlayerAchievement progress;
		var completedNow = false;

		lock (player.ProgressLock)
		{
			if (!player.Achievements.TryGetValue(achievement.Id, out progress!))
			{
				progress = new PlayerAchievement
				{
					AchievementId = achievement.Id
				};
				player.Achievements[achievement.Id] = progress;
			}

			if (progress.IsCompleted)
				return;

			progress.Progress = Math.Min(achievement.Amount, progress.Progress + amount);

			if (progress.Progress >= achievement.Amount)
			{
				progress.IsCompleted = true;
				progress.CompletedAt = DateTime.UtcNow;
				completedNow = true;
			}
		}

		if (completedNow)
		{
			CompleteAchievement(player, achievement, progress);
			return;
		}

		SaveProgress(player.SteamId, progress);
	}

	public async Task SaveAllPlayersAsync()
	{
		foreach (var player in _players.Values.Where(player => player.IsLoaded))
		{
			await SaveProgressSnapshotAsync(player.SteamId, Snapshot(player));
		}
	}

	public void Clear()
	{
		_players.Clear();
		RefreshActivePlayerCount();
	}

	private async Task LoadPlayerDataAsync(AchievementPlayer player)
	{
		var rows = await _database.GetPlayerProgressAsync(player.SteamId, _getConfig().SeasonKey);

		lock (player.ProgressLock)
		{
			player.Achievements.Clear();

			foreach (var row in rows)
			{
				player.Achievements[row.AchievementId] = PlayerAchievement.FromDb(row);
			}

			player.IsLoaded = true;
		}
	}

	private void CompleteAchievement(
		AchievementPlayer player,
		AchievementDefinition achievement,
		PlayerAchievement progress)
	{
		_ = Task.Run(async () =>
		{
			try
			{
				await _database.CompleteAchievementAsync(
					player.SteamId,
					achievement.Id,
					_getConfig().SeasonKey,
					achievement.Amount);

				AchievementsSW2.Core.Scheduler.NextWorldUpdate(() =>
				{
					if (!player.IsValid)
						return;

					foreach (var command in achievement.RewardCommands)
					{
						var replaced = ReplacePlaceholders(player.Player, command);
						try
						{
							AchievementsSW2.Core.Engine.ExecuteCommand(replaced);
						}
						catch (Exception ex)
						{
							AchievementsSW2.Core.Logger.LogError(
								ex,
								"Reward command failed for achievement {AchievementId} and player {SteamId}: {Command}",
								achievement.Id,
								player.SteamId,
								replaced);
						}
					}

					player.Player.SendChat($"[Achievements] Completed: {achievement.Name}");
					OnAchievementCompleted?.Invoke(player, achievement, progress);
				});
			}
			catch (Exception ex)
			{
				AchievementsSW2.Core.Logger.LogError(
					ex,
					"Failed to complete achievement {AchievementId} for {SteamId}",
					achievement.Id,
					player.SteamId);
			}
		});
	}

	private void SaveProgress(ulong steamId, PlayerAchievement progress)
	{
		_ = Task.Run(async () =>
		{
			await _database.SaveProgressAsync(
				steamId,
				progress.AchievementId,
				_getConfig().SeasonKey,
				progress.Progress,
				progress.IsCompleted,
				progress.CompletedAt);
		});
	}

	private async Task SaveProgressSnapshotAsync(ulong steamId, IEnumerable<PlayerAchievement> progressRows)
	{
		foreach (var progress in progressRows)
		{
			await _database.SaveProgressAsync(
				steamId,
				progress.AchievementId,
				_getConfig().SeasonKey,
				progress.Progress,
				progress.IsCompleted,
				progress.CompletedAt);
		}
	}

	private static List<PlayerAchievement> Snapshot(AchievementPlayer player)
	{
		lock (player.ProgressLock)
		{
			return player.Achievements.Values
				.Select(progress => new PlayerAchievement
				{
					AchievementId = progress.AchievementId,
					Progress = progress.Progress,
					IsCompleted = progress.IsCompleted,
					CompletedAt = progress.CompletedAt
				})
				.ToList();
		}
	}

	private void RefreshActivePlayerCount()
	{
		_activePlayerCount = _players.Values.Count(player => player.IsValid);
	}

	private static bool MatchesAchievement(
		AchievementDefinition achievement,
		string eventType,
		string target,
		Dictionary<string, object?>? eventProperties)
	{
		if (!string.Equals(achievement.Event, eventType, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(achievement.Target, target, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (achievement.EventProperties is not { Count: > 0 })
			return true;

		if (eventProperties == null)
			return false;

		foreach (var (key, achievementValue) in achievement.EventProperties)
		{
			if (!eventProperties.TryGetValue(key, out var eventValue) || eventValue == null)
				return false;

			if (!ComparePropertyValue(achievementValue, eventValue))
				return false;
		}

		return true;
	}

	private static bool ComparePropertyValue(JsonElement achievementValue, object eventValue)
	{
		return achievementValue.ValueKind switch
		{
			JsonValueKind.True or JsonValueKind.False =>
				eventValue is bool eventBool && achievementValue.GetBoolean() == eventBool,

			JsonValueKind.Number when achievementValue.TryGetInt64(out var achievementLong) =>
				CompareNumericValue(eventValue, achievementLong),

			JsonValueKind.Number when achievementValue.TryGetDouble(out var achievementDouble) =>
				CompareFloatingPointValue(eventValue, achievementDouble),

			JsonValueKind.String when achievementValue.GetString() is { } achievementString =>
				eventValue is string eventString &&
				eventString.Contains(achievementString, StringComparison.OrdinalIgnoreCase),

			_ => false
		};
	}

	private static bool CompareNumericValue(object eventValue, long achievementValue)
	{
		return eventValue switch
		{
			byte value => value >= achievementValue,
			sbyte value => value >= achievementValue,
			short value => value >= achievementValue,
			ushort value => value >= achievementValue,
			int value => value >= achievementValue,
			uint value => value >= achievementValue,
			long value => value >= achievementValue,
			ulong value => value >= (ulong)achievementValue,
			float value => value >= achievementValue,
			double value => value >= achievementValue,
			decimal value => value >= achievementValue,
			_ => false
		};
	}

	private static bool CompareFloatingPointValue(object eventValue, double achievementValue)
	{
		return eventValue switch
		{
			float value => value >= achievementValue,
			double value => value >= achievementValue,
			decimal value => (double)value >= achievementValue,
			byte value => value >= achievementValue,
			sbyte value => value >= achievementValue,
			short value => value >= achievementValue,
			ushort value => value >= achievementValue,
			int value => value >= achievementValue,
			uint value => value >= achievementValue,
			long value => value >= achievementValue,
			ulong value => value >= achievementValue,
			_ => false
		};
	}

	private static string ReplacePlaceholders(IPlayer player, string command)
	{
		var replacements = new Dictionary<string, string>
		{
			{ "{slot}", player.Slot.ToString() },
			{ "{userid}", player.PlayerID.ToString() },
			{ "{name}", player.Controller?.PlayerName ?? "Unknown" },
			{ "{steamid64}", player.SteamID.ToString() },
			{ "{steamid}", player.SteamID.ToString() },
			{ "u0022", "\"" }
		};

		foreach (var (key, value) in replacements)
		{
			command = command.Replace(key, value);
		}

		return command;
	}

	public event Action<AchievementPlayer, AchievementDefinition, PlayerAchievement>? OnAchievementCompleted;
}
