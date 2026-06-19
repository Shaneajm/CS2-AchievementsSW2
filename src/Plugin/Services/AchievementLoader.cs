using System.Net.Http;
using System.Text.Json;
using AchievementsSW2.Plugin.Config;
using AchievementsSW2.Plugin.Models;
using Microsoft.Extensions.Logging;

namespace AchievementsSW2.Plugin.Services;

public sealed class AchievementLoader(ILogger logger)
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};
	private static readonly HttpClient HttpClient = new();

	private readonly ILogger _logger = logger;
	private volatile AchievementDefinition[] _achievements = [];

	public IReadOnlyList<AchievementDefinition> Achievements => _achievements;

	public async Task<AchievementLoadResult> LoadAsync(string pluginPath, PluginConfig config)
	{
		var localPath = Path.Combine(pluginPath, "resources", "achievements.json");

		if (config.AchievementSource == AchievementSource.Remote)
		{
			var remoteResult = await TryLoadRemoteAsync(config);
			if (remoteResult.Achievements.Count > 0)
			{
				ReplaceAchievements(remoteResult.Achievements);
				return remoteResult;
			}

			_logger.LogWarning(
				"Remote achievement load failed or returned no valid achievements. Falling back to {Path}.",
				localPath);
		}

		var localResult = await LoadFromFileAsync(localPath);
		ReplaceAchievements(localResult.Achievements);
		return localResult;
	}

	public IEnumerable<AchievementDefinition> GetActiveAchievements(string serverType, string? mapName = null)
	{
		return _achievements.Where(achievement =>
			achievement.IsActiveForServerType(serverType) &&
			achievement.IsActiveForMap(mapName));
	}

	private void ReplaceAchievements(IReadOnlyList<AchievementDefinition> achievements)
	{
		_achievements = achievements.ToArray();
	}

	private static async Task<AchievementLoadResult> LoadFromFileAsync(string filePath)
	{
		if (!File.Exists(filePath))
		{
			return new AchievementLoadResult(
				[],
				[$"Achievement catalog was not found at {filePath}."],
				filePath);
		}

		try
		{
			var json = await File.ReadAllTextAsync(filePath);
			return LoadFromJson(json, filePath);
		}
		catch (Exception ex)
		{
			return new AchievementLoadResult(
				[],
				[$"Failed to read achievement catalog at {filePath}: {ex.Message}"],
				filePath);
		}
	}

	private static async Task<AchievementLoadResult> TryLoadRemoteAsync(PluginConfig config)
	{
		if (string.IsNullOrWhiteSpace(config.RemoteUrl))
		{
			return new AchievementLoadResult(
				[],
				["Remote achievement source is selected, but RemoteUrl is empty."],
				"remote");
		}

		try
		{
			using var timeout = new CancellationTokenSource(
				TimeSpan.FromSeconds(Math.Max(1, config.RemoteTimeoutSeconds)));
			var json = await HttpClient.GetStringAsync(config.RemoteUrl, timeout.Token);
			return LoadFromJson(json, config.RemoteUrl);
		}
		catch (Exception ex)
		{
			return new AchievementLoadResult(
				[],
				[$"Failed to fetch remote achievement catalog from {config.RemoteUrl}: {ex.Message}"],
				config.RemoteUrl);
		}
	}

	private static AchievementLoadResult LoadFromJson(string json, string sourceDescription)
	{
		try
		{
			var definitions = JsonSerializer.Deserialize<List<AchievementDefinition>>(json, JsonOptions) ?? [];
			var errors = new List<string>();
			var valid = new List<AchievementDefinition>();
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			for (var i = 0; i < definitions.Count; i++)
			{
				var achievement = definitions[i];
				var validationErrors = Validate(achievement, i);

				if (validationErrors.Count > 0)
				{
					errors.AddRange(validationErrors);
					continue;
				}

				if (!ids.Add(achievement.Id))
				{
					errors.Add($"Achievement '{achievement.Id}' is duplicated and was skipped.");
					continue;
				}

				valid.Add(achievement);
			}

			if (valid.Count == 0 && errors.Count == 0)
			{
				errors.Add($"Achievement catalog at {sourceDescription} did not contain any achievements.");
			}

			return new AchievementLoadResult(valid, errors, sourceDescription);
		}
		catch (JsonException ex)
		{
			return new AchievementLoadResult(
				[],
				[$"Failed to parse achievement catalog at {sourceDescription}: {ex.Message}"],
				sourceDescription);
		}
	}

	private static List<string> Validate(AchievementDefinition achievement, int index)
	{
		var errors = new List<string>();
		var label = string.IsNullOrWhiteSpace(achievement.Id)
			? $"achievement at index {index}"
			: $"achievement '{achievement.Id}'";

		if (string.IsNullOrWhiteSpace(achievement.Id))
			errors.Add($"{label} is missing Id.");

		if (string.IsNullOrWhiteSpace(achievement.Name))
			errors.Add($"{label} is missing Name.");

		if (string.IsNullOrWhiteSpace(achievement.Event))
			errors.Add($"{label} is missing Event.");

		if (string.IsNullOrWhiteSpace(achievement.Target))
			errors.Add($"{label} is missing Target.");

		if (achievement.Amount <= 0)
			errors.Add($"{label} must have Amount greater than 0.");

		return errors;
	}
}
