using AchievementsSW2.Plugin.Models;

namespace AchievementsSW2.Plugin.Services;

public sealed record AchievementLoadResult(
	IReadOnlyList<AchievementDefinition> Achievements,
	IReadOnlyList<string> Errors,
	string SourceDescription);
