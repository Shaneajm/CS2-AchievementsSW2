namespace AchievementsSW2.Plugin.Config;

public sealed class PluginConfig
{
	public string DatabaseConnection { get; set; } = "host";

	public List<string> Commands { get; set; } = ["achievements", "achievement", "ach"];

	public string AdminReloadCommand { get; set; } = "achievements_reload";

	public string ServerType { get; set; } = "default";

	public AchievementSource AchievementSource { get; set; } = AchievementSource.Local;

	public string RemoteUrl { get; set; } = string.Empty;

	public int RemoteTimeoutSeconds { get; set; } = 10;

	public string? SeasonKey { get; set; }

	public int MinimumPlayers { get; set; } = 4;

	public bool AllowProgressDuringWarmup { get; set; }

	public bool EventDebugLogs { get; set; }
}
