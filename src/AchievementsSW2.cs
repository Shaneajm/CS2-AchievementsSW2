using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AchievementsSW2.Plugin.Config;
using AchievementsSW2.Plugin.Services;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
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
  private CancellationTokenSource? _playtimeTimerCts;
  private readonly Dictionary<string, HashSet<string>> _registeredEvents = new(StringComparer.OrdinalIgnoreCase);
  private string _currentMapName = string.Empty;

  private static readonly ConcurrentDictionary<Type, PropertyInfo?> AccessorPropertyCache = new();
  private static readonly ConcurrentDictionary<(Type Type, string Target), PropertyInfo?> PlayerPropertyCache = new();
  private static readonly ConcurrentDictionary<Type, PropertyInfo[]> EventPropertiesCache = new();

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
    RegisterAchievementEvents();
    StartPlaytimeTimer();

    if (hotReload)
    {
      HandleHotReload();
    }
  }

  public override void Unload() {
    _playtimeTimerCts?.Cancel();
    _playtimeTimerCts = null;

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
    RegisterAchievementEvents();
    StartPlaytimeTimer();
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

    _database.InitializeAsync()
      .GetAwaiter()
      .GetResult();
  }

  private void RegisterEventHandlers()
  {
    Core.GameEvent.HookPost<EventPlayerActivate>(OnPlayerActivate);
    Core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
    Core.GameEvent.HookPost<EventRoundEnd>(OnRoundEndSave);
    Core.Event.OnMapLoad += OnMapLoad;
  }

  private void RegisterAchievementEvents()
  {
    foreach (var achievement in _achievementLoader.Achievements)
    {
      if (string.Equals(achievement.Event, "PlayTime", StringComparison.OrdinalIgnoreCase))
        continue;

      if (string.Equals(achievement.Event, "CustomMapWin", StringComparison.OrdinalIgnoreCase))
      {
        Core.Logger.LogInformation(
          "Achievement {AchievementId} uses CustomMapWin, which is currently skipped.",
          achievement.Id);
        continue;
      }

      if (_registeredEvents.TryGetValue(achievement.Event, out var targets))
      {
        targets.Add(achievement.Target);
        continue;
      }

      if (RegisterEventForAchievement(achievement.Event))
      {
        _registeredEvents[achievement.Event] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
          achievement.Target
        };
      }
    }

    Core.Logger.LogInformation(
      "Registered {Count} achievement event type(s).",
      _registeredEvents.Count);
  }

  private bool RegisterEventForAchievement(string eventName)
  {
    var eventType = AppDomain.CurrentDomain.GetAssemblies()
      .Select(assembly => assembly.GetType($"SwiftlyS2.Shared.GameEventDefinitions.{eventName}"))
      .FirstOrDefault(type => type != null);

    if (eventType == null)
    {
      Core.Logger.LogWarning("Achievement event type {Event} was not found.", eventName);
      return false;
    }

    var gameEventInterface = eventType.GetInterfaces()
      .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IGameEvent<>));

    if (gameEventInterface == null)
    {
      Core.Logger.LogWarning("Achievement event type {Event} does not implement IGameEvent<T>.", eventName);
      return false;
    }

    try
    {
      var hookPostMethod = typeof(IGameEventService).GetMethod(nameof(IGameEventService.HookPost));
      var handlerMethod = GetType().GetMethod(
        nameof(OnGenericAchievementEvent),
        BindingFlags.NonPublic | BindingFlags.Instance);

      if (hookPostMethod == null || handlerMethod == null)
        return false;

      var genericHookPost = hookPostMethod.MakeGenericMethod(eventType);
      var genericHandler = handlerMethod.MakeGenericMethod(eventType);
      var delegateType = typeof(IGameEventService.GameEventHandler<>).MakeGenericType(eventType);
      var handlerDelegate = Delegate.CreateDelegate(delegateType, this, genericHandler);

      genericHookPost.Invoke(Core.GameEvent, [handlerDelegate]);
      return true;
    }
    catch (Exception ex)
    {
      Core.Logger.LogError(ex, "Failed to register achievement event handler for {Event}.", eventName);
      return false;
    }
  }

  private HookResult OnGenericAchievementEvent<T>(T @event) where T : IGameEvent<T>
  {
    var eventType = typeof(T).Name;

    if (!Config.CurrentValue.AllowProgressDuringWarmup)
    {
      var gameRules = Core.EntitySystem.GetGameRules();
      if (gameRules?.WarmupPeriod == true)
        return HookResult.Continue;
    }

    if (eventType == nameof(EventRoundEnd))
    {
      ProcessRoundEndAchievements(@event);
      return HookResult.Continue;
    }

    if (!_registeredEvents.TryGetValue(eventType, out var targets))
      return HookResult.Continue;

    var eventProperties = ExtractEventProperties(@event);

    if (Config.CurrentValue.EventDebugLogs)
    {
      foreach (var (key, value) in eventProperties)
      {
        Core.Logger.LogInformation("[{Event}] {Property}: {Value}", eventType, key, value);
      }
    }

    var accessorProperty = AccessorPropertyCache.GetOrAdd(typeof(T), static type =>
      type.GetProperty("Accessor")
        ?? type.GetInterfaces()
          .SelectMany(static iface => new[] { iface }.Concat(iface.GetInterfaces()))
          .Select(static iface => iface.GetProperty("Accessor"))
          .FirstOrDefault(property => property != null));

    foreach (var target in targets)
    {
      var player = GetEventPlayer(@event, accessorProperty, target);
      if (player?.IsValid != true || player.IsFakeClient)
        continue;

      _playerAchievementManager.ProcessEvent(eventType, target, player, eventProperties, _currentMapName);
    }

    return HookResult.Continue;
  }

  private static IPlayer? GetEventPlayer<T>(T @event, PropertyInfo? accessorProperty, string target)
  {
    if (accessorProperty?.GetValue(@event) is IGameEventAccessor accessor)
    {
      var player = accessor.GetPlayer(target.ToLowerInvariant());
      if (player != null)
        return player;
    }

    var playerProperty = PlayerPropertyCache.GetOrAdd((typeof(T), target), static key =>
      key.Type.GetProperty($"{key.Target}Player"));

    return playerProperty?.GetValue(@event) as IPlayer;
  }

  private static Dictionary<string, object?> ExtractEventProperties<T>(T @event)
  {
    var properties = EventPropertiesCache.GetOrAdd(typeof(T), static type =>
      type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.CanRead)
        .ToArray());

    var values = new Dictionary<string, object?>(properties.Length);

    foreach (var property in properties)
    {
      values[property.Name] = property.GetValue(@event);
    }

    return values;
  }

  private void ProcessRoundEndAchievements<T>(T @event)
  {
    var winnerProperty = typeof(T).GetProperty("Winner");
    if (winnerProperty == null)
      return;

    var winnerValue = winnerProperty.GetValue(@event);
    var winner = Convert.ToInt32(winnerValue ?? 0);
    if (winner <= (int)Team.Spectator)
      return;

    foreach (var achievementPlayer in _playerAchievementManager.AllPlayers.Where(player => player.IsValid))
    {
      var playerTeam = (int)(achievementPlayer.Player.Controller?.Team ?? Team.None);
      if (playerTeam <= (int)Team.Spectator)
        continue;

      var target = playerTeam == winner ? "winner" : "loser";
      _playerAchievementManager.ProcessEvent(nameof(EventRoundEnd), target, achievementPlayer.Player, null, _currentMapName);
    }
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

  private void OnMapLoad(IOnMapLoadEvent @event)
  {
    _currentMapName = @event.MapName;
  }

  private void StartPlaytimeTimer()
  {
    _playtimeTimerCts?.Cancel();
    _playtimeTimerCts = null;

    var hasPlaytimeAchievements = _achievementLoader.Achievements.Any(achievement =>
      string.Equals(achievement.Event, "PlayTime", StringComparison.OrdinalIgnoreCase));

    if (!hasPlaytimeAchievements)
      return;

    _playtimeTimerCts = Core.Scheduler.RepeatBySeconds(60f, ProcessPlaytimeAchievements);
  }

  private void ProcessPlaytimeAchievements()
  {
    foreach (var player in _playerAchievementManager.AllPlayers.Where(player => player is { IsLoaded: true, IsValid: true }))
    {
      _playerAchievementManager.ProcessEvent(
        "PlayTime",
        "Userid",
        player.Player,
        null,
        _currentMapName);
    }
  }

  private void HandleHotReload()
  {
    Core.Scheduler.NextWorldUpdate(() =>
    {
      foreach (var player in Core.PlayerManager.GetAllPlayers())
      {
        if (player.IsValid && !player.IsFakeClient)
        {
          _playerAchievementManager.GetOrCreatePlayer(player);
        }
      }
    });
  }
} 
