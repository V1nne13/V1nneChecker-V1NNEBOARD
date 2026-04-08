# VenneChecker (V1NNEBOARD)

A BepInEx mod for Gorilla Tag — the most comprehensive player mod checker available. Features a physical 3D hand menu with laser pointer scanner and 25 detection methods covering Harmony patches, loaded assemblies, network behavior, physics anomalies, and per-player rig validation.

## Features

### Menu System
- **Physical 3D hand menu** attached to your left hand (hold X to open)
- **Red laser pointer** from your right hand to aim at players
- **Finger touch interaction** — press buttons with your right index finger
- **Multi-page board** — Home, Player List, Mod Checker, Room Control, Settings

### Pages
- **Home** — overview and status
- **Player List** — all players in the room, mute functionality
- **Mod Checker** — scan any player with the laser for full analysis (player info, suspicious behavior, detected mods, cosmetics)
- **Room Control** — server hop, disconnect, reconnect
- **Settings** — push-to-talk toggle, sound toggle, laser color (red, green, blue, purple, yellow, cyan)

### Detection System (25 methods)

#### Photon Network Scanning
1. **CustomProperties scan** — checks remote players' Photon properties against 69+ known cheat keys
2. **Cheat database** — built-in database with user-extensible `VenneChecker_Cheats.txt`

#### Harmony / Assembly Detection
3. **Harmony ID audit** — checks 42 known cheat Harmony IDs via `Harmony.HasAnyPatches()`
4. **Harmony patch enumeration** — enumerates all patched methods, flags unknown patches on 25+ critical game methods (MonkeAgent, VRRig.IsPositionInRange, GTPlayer.AntiTeleportTechnology, telemetry, etc.)
5. **BepInEx Chainloader scan** — reads `Chainloader.PluginInfos` and checks against 18 known cheat GUIDs
6. **BepInEx Manager GameObject** — inspects all components on the BepInEx_Manager object
7. **AppDomain assembly scan** — enumerates all loaded assemblies, checks types against cheat namespace database (catches renamed/obfuscated DLLs)
8. **Scene MonoBehaviour scan** — finds injected cheat components (mod menus, ESP, etc.) in the scene
9. **BepInEx dependency errors** — detects failed cheat plugin loads
10. **Loaded assembly namespace scan** — checks all type namespaces in loaded assemblies
11. **Local plugin folder scan** — scans `BepInEx/plugins/` directory

#### Known Cheat Mods Detected
Seralyth, Juul, Saturn, ForeverCosmetx, Astre/XENON, CanvasGUI/Euph, Chqser, Control, NXO Remastered, Nylox, GKong, Obsidian, Genesis, Elux, Violet, Cronos, Orbit, Elixir, Colossal, Malachi, SpeedboostMod, WalkSim, GrayScreen, Lusid Pull, Unlock V.I.M., FortniteEmoteWheel, and more.

#### Network Behavior Detection
12. **RPC flood detection** — flags >40 RPCs/sec sustained
13. **Master client exploit** — flags >3 SetMasterClient events from same actor in 5s
14. **Instantiate exploit** — event code 200 from non-master
15. **Destroy exploit** — event code 201 from non-master

#### Movement Tracking
16. **Speed hack** — flags velocity >25 m/s sustained (normal max ~15 m/s)
17. **Teleport detection** — flags >40 units in one sample
18. **Fly hack** — flags Y gains >5m over 3 samples with <2m horizontal movement
- Requires 3 suspicious readings before flagging (prevents false positives from lag)
- Shows notification when player first flagged

#### Per-Player Rig Validation
19. **Tag distance validation** — flags hands >4m from head (impossible reach)
20. **Color validation** — flags out-of-range RGB values (<0 or >1)
21. **Scale anomaly** — flags transform scale outside 0.3-2.0 range
22. **Arm length anomaly** — flags arm length multiplier >1.6 or <0.4
23. **PhotonView ownership** — flags Creator != Owner (view hijacking)
24. **Name mismatch** — compares VRRig displayed name vs Photon NickName

#### Other
25. **Low FPS warning** — flags players under 60 Hz
- **Cosmetic display** — shows equipped cosmetics in the mod list
- **Platform detection** — identifies Steam/PCVR vs Quest players
- **Room auto-scan** — automatically scans all players and shows cheat alerts

## Installation

1. Make sure [BepInEx 5.x](https://github.com/BepInEx/BepInEx) is installed for Gorilla Tag
2. Copy `VenneChecker.dll` into your Gorilla Tag's `BepInEx/plugins/VenneChecker/` folder
3. Launch Gorilla Tag

## Controls

| Action | Button |
|--------|--------|
| Open menu | Hold **X** (left controller) |
| Close menu | Release **X** |
| Aim laser | Point right hand (laser appears on Mod Checker page) |
| Scan player | Hold **Right Trigger** while aiming at a player |
| Press buttons | Touch with right index finger |

## Mod Checker Display

When you scan a player, the Mod Checker page shows:

```
PLAYER: PlayerName
PLATFORM: Steam / PCVR
FPS: 72
JOINED: 5m ago

   SUSPICIOUS BEHAVIOR
 ! Speed Hack (3x)
 ! Low FPS: 42 Hz

     DETECTED MODS
 ! Seralyth Menu          (red = cheat)
 Cosmetic: AdminBadge     (blue = info)
 Cheat Harmony ID: Juul   (red = cheat)
```

## Configuration

### Cheat Database
**File:** `BepInEx/config/VenneChecker_Cheats.txt`

Add one mod name per line. Lines starting with `#` are comments. Case-insensitive.

### Settings
**File:** `BepInEx/config/VenneChecker_Settings.txt`

Persists push-to-talk, sound, and laser color preferences.

## Building from Source

1. Open the project in Visual Studio or use `dotnet build`
2. Ensure DLL references in `VenneChecker.csproj` point to your Gorilla Tag installation
3. Targets `netstandard2.1`
4. The built DLL is automatically copied to `BepInEx/plugins/VenneChecker/`

## Project Structure

```
VenneChecker/
  VenneChecker.cs          — Plugin entry point
  UI/
    MenuManager.cs         — Menu lifecycle & page switching
    BoardPage.cs           — Base class for all pages
    BoardButton.cs         — 3D button component
    HomePage.cs            — Home page
    PlayerListPage.cs      — Player list & mute
    ModCheckerPage.cs      — Scan results display
    RoomControlPage.cs     — Server hop, disconnect, reconnect
    SettingsPage.cs        — Settings with save/load
    LaserPointer.cs        — Laser aim & VRRig detection
    FingerTouch.cs         — Finger-based button pressing
  Detection/
    PlayerScanner.cs       — Core scan logic, wires all detectors
    CheatDatabase.cs       — Known cheat keys & namespaces
    HarmonyDetector.cs     — Harmony/BepInEx/assembly scanning
    BehaviorDetector.cs    — Per-player rig anomaly checks
    MovementTracker.cs     — Speed/teleport/fly tracking
    NetworkEventDetector.cs — RPC/event monitoring via Harmony
    CosmeticChecker.cs     — Equipped cosmetic reading
    RoomScanner.cs         — Auto-scan all players in room
  Utils/
    SoundManager.cs        — Procedural audio (beeps)
    NotificationManager.cs — HUD notifications
    DelayedAction.cs       — Timer-based callbacks
    Log.cs                 — Logging wrapper
```

## Requirements

- Gorilla Tag (latest version)
- BepInEx 5.x
- Oculus / Meta Quest or SteamVR
