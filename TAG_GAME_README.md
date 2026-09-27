# Tag Game — Unity + Unity Gaming Services

A tag game with three ways to play, all sharing one leaderboard:

1. **Practice** — offline, you vs. AI taggers (`Assets/Scripts/Gameplay/`)
2. **Online** — real players over the internet via Unity Relay + Lobby, no port forwarding (`Networking/LobbyRelayService.cs`)
3. **Wireless / LAN** — real players on the same WiFi network, no internet required (`Networking/LanDiscoveryService.cs`)

This was generated as a file set (no Unity Editor is available in this cloud
workspace), so you'll import it into a Unity project and finish wiring it up
in the Editor. Steps below.

## 1. Import

1. Create or open a Unity 6.x project.
2. Copy `Assets/` from this package into your project's `Assets/` folder.
3. Open **Window > Package Manager > Add package by name** and add each
   package listed in `manifest-additions.json` (or merge that file's
   `dependencies` block directly into `Packages/manifest.json`).
4. In **Project Settings > Services**, link the project to a Unity Cloud
   project (create one at dashboard.unity3d.com if you don't have one).

## 2. Scene setup — Practice mode (offline vs AI)

1. New scene, add a ground plane and a few obstacles if you want.
2. Add an empty GameObject `Bootstrap` with `UGSBootstrap.cs`,
   `RemoteConfigManager.cs`, and `LeaderboardManager.cs` on it.
3. Add a Player capsule with a `CharacterController` and `PlayerController.cs`.
4. Create a `TaggerAI` prefab: capsule + `TaggerAI.cs` (no CharacterController
   needed — it moves via transform).
5. Add an empty `GameManager` GameObject with `TagGameManager.cs`; drag in
   the player, the tagger prefab, and 2–4 empty Transforms as
   `taggerSpawnPoints`.
6. Press Play. The manager pulls difficulty from Remote Config (falls back to
   the Inspector defaults if unreachable), spawns taggers, and submits your
   survival time through Cloud Code when the round ends.

## 3. Scene setup — Online & Wireless (real players)

Both modes reuse the same networked prefab and game manager; only the
*connection* step differs.

1. Add a `NetworkManager` GameObject (Netcode for GameObjects adds this
   component). Add a `UnityTransport` component alongside it if it isn't
   already there.
2. Build a **Player prefab**: capsule + `CharacterController` +
   `NetworkObject` + `NetworkPlayerController.cs`. Assign it as the
   NetworkManager's **Player Prefab**.
3. Add `NetworkTagGameManager.cs` to a `NetworkObject`-tagged GameObject in
   the scene (e.g. on the NetworkManager itself, or a dedicated
   `GameManager` object with a `NetworkObject` component) so it spawns for
   every connected client.
4. Add `LobbyRelayService.cs` and `LanDiscoveryService.cs` to your
   persistent `Bootstrap` object (alongside `UGSBootstrap`).
5. Build a menu Canvas and wire buttons to `MultiplayerMenuUI.cs`:
   - **Host Online** → `OnClickHostOnline()` — shows a short code to share
   - **Join Online** → `OnClickJoinOnline()` (needs the code)
   - **Quick Match** → `OnClickQuickMatch()` — joins any open public lobby
   - **Host on this WiFi** → `OnClickHostLan()`
   - **Scan for nearby games** → `OnClickScanForLanGames()` — populates a
     list of discovered LAN hosts to tap and join

### Online vs. Wireless — when to use which

| | Online (Relay + Lobby) | Wireless (LAN) |
|---|---|---|
| Needs internet | Yes | No — same WiFi/hotspot is enough |
| NAT / port forwarding | Not needed (Relay handles it) | Not needed (direct LAN IP) |
| Discovery | Lobby code or Quick Match | UDP broadcast, auto-discovered |
| Depends on Unity Gaming Services | Yes | No (works fully offline) |

Build both for the widest reach: Online for friends anywhere, Wireless for
same-room play (couch multiplayer, LAN parties, classrooms) with zero data
usage.

## 4. Deploy cloud resources

Open **Services > Deployment** in the Editor and deploy, in this order:

1. `RemoteConfig/TagGameConfig.rc` — tunable difficulty values
2. `AccessControl/tag_game.ac` — denies direct player writes to the
   leaderboard so only the Cloud Code module can submit scores
3. `Leaderboards/tag_survival_leaderboard.lb` — the leaderboard itself
4. `CloudCode/SubmitTagScore/` — the score-submission module

**Note on the Cloud Code module:** the Deployment window generates the
`.sln`/`.csproj` scaffold for you the first time you create a Cloud Code
module in the Editor (Services > Deployment > right-click > Create > Cloud
Code Script/Module). Create a module named `TagGameModule`, then replace its
generated `.cs` file with `CloudCode/SubmitTagScore/SubmitTagScore.cs` from
this package, and point its `.ccmr` at the generated solution file.

## 5. How the anti-cheat / server-authority works

- **Practice mode:** the client still can't write the leaderboard directly —
  `LeaderboardManager.SubmitSurvivalTimeAsync` always goes through the
  `SubmitTagScore` Cloud Code function, which rejects implausible times.
- **Online/Wireless:** tag detection happens on the *owning* client (for
  responsiveness) but is re-validated on the **server** — distance check and
  "are you actually It right now" check — before `IsIt` (a server-write-only
  `NetworkVariable`) changes. A modified client can request a tag it
  shouldn't get; the server just ignores it.
- Either way, `tag_game.ac` denies `Player` principals from writing scores
  directly, so even a fully compromised client can only ever call the Cloud
  Code endpoint, never the leaderboard API itself.

## 6. Testing multiplayer locally

- **Online:** use two Editor instances via **ParrelSync** (or build once and
  run the build + Editor together) — both need internet access; Relay
  handles NAT for you even on the same machine.
- **Wireless:** run one instance as a build (or a second machine on the same
  WiFi), host on one, scan-and-join on the other. No internet connection is
  required for this path — it will work on a WiFi network with no uplink at
  all (e.g., a phone hotspot with data off, or a router not connected to the
  internet).

## File map

```
Assets/Scripts/Gameplay/     Practice mode (offline, vs AI)
Assets/Scripts/Networking/   Real player-vs-player tag (Online + Wireless)
Assets/Scripts/Services/     UGS bootstrap, Remote Config, Leaderboards
Assets/Scripts/UI/           Menu wiring
CloudCode/SubmitTagScore/    Server-authoritative score submission
RemoteConfig/                Difficulty/tuning definitions (.rc)
Leaderboards/                Leaderboard definition (.lb)
AccessControl/               Locks leaderboard writes to Cloud Code only (.ac)
manifest-additions.json      Packages to add to your project
```
