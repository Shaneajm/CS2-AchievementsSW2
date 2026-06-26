<div align="center">
  <img src="https://pan.samyyc.dev/s/VYmMXE" />
  <h2><strong>AchievementsSW2</strong></h2>
  <h3>An acheivements system for SwifltyS2 CS2 servers.</h3>
</div>

<p align="center">
  <img src="https://img.shields.io/badge/build-passing-brightgreen" alt="Build Status">
  <img src="https://img.shields.io/github/downloads/AVERAGE/AchievementsSW2/total" alt="Downloads">
  <img src="https://img.shields.io/github/stars/AVERAGE/AchievementsSW2?style=flat&logo=github" alt="Stars">
  <img src="https://img.shields.io/github/license/AVERAGE/AchievementsSW2" alt="License">
</p>

## Info
This plugin is a lightweight acheivements system for CS2 servers that want to have a system seperate from something like a battlepass system. Server owners can create achievements for any server or specific servers/gamemodes. 

You can load the achievements from a local file or via a URL. Progress is saved in the database of your choice although only MySQL has been tested.

## Features
- Load achievements from a remote source (URL)
- Supports [CS2 Game Events](https://cs2.poggu.me/dumped-data/game-events/)
- Progress can be shared across mulitple servers.
- Achievements can be restricted to specific servers/gamemodes.
- Season support

## Config
```json
{
  "AchievementsSW2": {
    "DatabaseConnection": "default",
    "Commands": [
      "achievements",
      "achievement",
      "ach"
    ],
    "AdminReloadCommand": "achievements_reload",
    "ServerType": "default",
    "AchievementSource": 0,
    "RemoteUrl": "",
    "RemoteTimeoutSeconds": 10,
    "SeasonKey": null,
    "MinimumPlayers": 0,
    "AllowProgressDuringWarmup": false,
    "EventDebugLogs": false
  }
}
```

## Achievements Example
```json
[
  {
    "Id": "general_100_kills",
    "Name": "First Century",
    "Description": "Kill 100 enemy players.",
    "Category": "General",
    "ServerTypes": [
      "all"
    ],
    "Event": "EventPlayerDeath",
    "Target": "Attacker",
    "Amount": 100,
    "RewardCommands": [],
    "RewardPhrase": "",
    "Hidden": false
  },
  {
    "Id": "general_10_round_wins",
    "Name": "On a Roll",
    "Description": "Win 10 rounds.",
    "Category": "General",
    "ServerTypes": [
      "all"
    ],
    "Event": "EventRoundEnd",
    "Target": "winner",
    "Amount": 10,
    "RewardCommands": [
      "say u0022{name}u0022 has won 10 rounds in a row!"
    ],
    "RewardPhrase": "Get a shoutout!",
    "Hidden": false
  },
  {
    "Id": "retake_100_defuses",
    "Name": "Clutch Technician",
    "Description": "Defuse 100 bombs.",
    "Category": "Retake",
    "ServerTypes": [
      "retake"
    ],
    "Event": "EventBombDefused",
    "Target": "Userid",
    "Amount": 100,
    "RewardCommands": [
      "eco give {steamid64} 30.0 coins"
    ],
    "RewardPhrase": "30 coins",
    "Hidden": false
  }
```

## Building

- Open the project in your preferred .NET IDE (e.g., Visual Studio, Rider, VS Code).
- Build the project. The output DLL and resources will be placed in the `build/` directory.
- The publish process will also create a zip file for easy distribution.

## Publishing

- Use the `dotnet publish -c Release` command to build and package your plugin.
- Distribute the generated zip file or the contents of the `build/publish` directory.# CS2-AchievementsSW2

## Disclosure
AI was used in the creation of this plugin. I reviewed the code and did not let design decisions be outsourced to an agent.

## Special Thanks
This plugin wouldn't have been possible without the work and inspiration of these projects:
- [K4-Missions - K4ryuu](https://github.com/K4ryuu/K4-Missions-SwiftlyS2)
- [SwiflyS2](https://github.com/swiftly-solution/swiftlys2)
