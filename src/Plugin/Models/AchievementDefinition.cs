using System.Text.Json;

namespace AchievementsSW2.Plugin.Models;

public sealed class AchievementDefinition
{
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public string Category { get; set; } = "General";

	public List<string> ServerTypes { get; set; } = [];

	public string Event { get; set; } = string.Empty;

	public string Target { get; set; } = string.Empty;

	public int Amount { get; set; }

	public List<string> RewardCommands { get; set; } = [];

	public string RewardPhrase { get; set; } = string.Empty;

	public bool Hidden { get; set; }

	public Dictionary<string, JsonElement>? EventProperties { get; set; }

	public string? MapName { get; set; }

	public List<string> MapPrefixes { get; set; } = [];

	public string? Flag { get; set; }

	public bool IsActiveForServerType(string serverType)
	{
		if (ServerTypes.Count == 0)
			return true;

		return ServerTypes.Any(type =>
			string.Equals(type, "all", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(type, serverType, StringComparison.OrdinalIgnoreCase));
	}

	public bool IsActiveForMap(string? mapName)
	{
		if (string.IsNullOrWhiteSpace(MapName) && MapPrefixes.Count == 0)
			return true;

		if (string.IsNullOrWhiteSpace(mapName))
			return false;

		if (!string.IsNullOrWhiteSpace(MapName) &&
			string.Equals(MapName, mapName, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return MapPrefixes.Any(prefix =>
			mapName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
	}
}
