using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AchievementsSW2.Plugin.Config;
using AchievementsSW2.Plugin.Services;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Plugins;

namespace AchievementsSW2;

[PluginMetadata(Id = "AchievementsSW2", Version = "0.1.0", Name = "Achievements ", Author = "AVERAGE", Description = "Create achievements that players can get by doing custom tasks like kills, defuses, and more")]
public partial class AchievementsSW2 : BasePlugin {
  private const string ConfigFileName = "config.json";
  private const string ConfigSection = "AchievementsSW2";

  public static new ISwiftlyCore Core { get; private set; } = null!;
  public static IOptionsMonitor<PluginConfig> Config { get; private set; } = null!;

  private AchievementLoader _achievementLoader = null!;
  private DatabaseService _database = null!;
  private PlayerAchievementManager _playerAchievementManager = null!;

  public AchievementsSW2(ISwiftlyCore core) : base(core)
  {
  }

  public override void ConfigureSharedInterface(IInterfaceManager interfaceManager) {
  }

  public override void UseSharedInterface(IInterfaceManager interfaceManager) {
  }

  public override void Load(bool hotReload) {
    Core = base.Core;

    LoadConfiguration();

    _achievementLoader = new AchievementLoader(Core.Logger);
    LoadAchievements();
    InitializeDatabase();
    RegisterCommands();
    RegisterEventHandlers();
  }

  public override void Unload() {
    try
    {
      Task.Run(async () => await _playerAchievementManager.SaveAllPlayersAsync())
        .Wait(TimeSpan.FromSeconds(5));
    }
    catch (Exception ex)
    {
      Core.Logger.LogError(ex, "Failed to save achievement progress during unload.");
    }

    _playerAchievementManager.Clear();
  }

  private static void LoadConfiguration()
  {
    Core.Configuration
      .InitializeJsonWithModel<PluginConfig>(ConfigFileName, ConfigSection)
      .Configure(builder =>
      {
        builder.AddJsonFile(ConfigFileName, optional: false, reloadOnChange: true);
      });

    ServiceCollection services = new();
    services.AddSwiftly(Core)
      .AddOptionsWithValidateOnStart<PluginConfig>()
      .BindConfiguration(ConfigSection);

    var provider = services.BuildServiceProvider();
    Config = provider.GetRequiredService<IOptionsMonitor<PluginConfig>>();
  }

  private void RegisterCommands()
  {
    var command = Config.CurrentValue.AdminReloadCommand;
    if (!string.IsNullOrWhiteSpace(command))
    {
      Core.Command.RegisterCommand(command, OnReloadCommand, permission: "achievementssw2.admin");
    }
  }

  private void OnReloadCommand(ICommandContext ctx)
  {
    LoadAchievements();
    ctx.Reply($"Reloaded {_achievementLoader.Achievements.Count} achievement definitions.");
  }

  private void LoadAchievements()
  {
    try
    {
      var result = _achievementLoader.LoadAsync(Core.PluginPath, Config.CurrentValue)
        .GetAwaiter()
        .GetResult();

      foreach (var error in result.Errors)
      {
        Core.Logger.LogWarning("{Error}", error);
      }

      Core.Logger.LogInformation(
        "Loaded {Count} achievement definitions from {Source}.",
        result.Achievements.Count,
        result.SourceDescription);
    }
    catch (Exception ex)
    {
      Core.Logger.LogError(ex, "Failed to load achievement definitions.");
    }
  }

  private void InitializeDatabase()
  {
    _database = new DatabaseService(Config.CurrentValue.DatabaseConnection);
    _playerAchievementManager = new PlayerAchievementManager(
      _database,
      _achievementLoader,
      () => Config.CurrentValue);

    _ = Task.Run(async () =>
    {
      await _database.InitializeAsync();
    });
  }

  private void RegisterEventHandlers()
  {
    Core.GameEvent.HookPost<EventPlayerActivate>(OnPlayerActivate);
    Core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
    Core.GameEvent.HookPost<EventRoundEnd>(OnRoundEndSave);
  }

  private HookResult OnPlayerActivate(EventPlayerActivate @event)
  {
    var player = Core.PlayerManager.GetPlayer(@event.UserId);

    if (player?.IsValid != true || player.IsFakeClient)
      return HookResult.Continue;

    _playerAchievementManager.GetOrCreatePlayer(player);
    return HookResult.Continue;
  }

  private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event)
  {
    var player = Core.PlayerManager.GetPlayer(@event.UserId);

    if (player != null)
    {
      _playerAchievementManager.RemovePlayer(player.SteamID);
    }

    return HookResult.Continue;
  }

  private HookResult OnRoundEndSave(EventRoundEnd @event)
  {
    _ = Task.Run(async () =>
    {
      try { await _playerAchievementManager.SaveAllPlayersAsync(); }
      catch (Exception ex) { Core.Logger.LogError(ex, "Failed to save achievement progress at round end."); }
    });

    return HookResult.Continue;
  }
} 
