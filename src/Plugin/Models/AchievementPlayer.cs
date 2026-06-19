using SwiftlyS2.Shared.Players;

namespace AchievementsSW2.Plugin.Models;

public sealed class AchievementPlayer
{
	public required ulong SteamId { get; init; }

	public required IPlayer Player { get; init; }

	public readonly object ProgressLock = new();

	public Dictionary<string, PlayerAchievement> Achievements { get; } = new(StringComparer.OrdinalIgnoreCase);

	public volatile bool IsLoaded;

	public bool IsValid => Player.IsValid && !Player.IsFakeClient;
}
