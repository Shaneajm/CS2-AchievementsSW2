using Microsoft.Extensions.DependencyInjection;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared;

namespace AchievementsSW2;

[PluginMetadata(Id = "AchievementsSW2", Version = "0.1.0", Name = "Achievements ", Author = "AVERAGE", Description = "Create achievements that players can get by doing custom tasks like kills, defuses, and more")]
public partial class AchievementsSW2 : BasePlugin {
  public AchievementsSW2(ISwiftlyCore core) : base(core)
  {
  }

  public override void ConfigureSharedInterface(IInterfaceManager interfaceManager) {
  }

  public override void UseSharedInterface(IInterfaceManager interfaceManager) {
  }

  public override void Load(bool hotReload) {
    
  }

  public override void Unload() {
  }
} 