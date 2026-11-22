# Multiplayer Deterministic Mode

## Status

- **State:** Implemented for RimWorld 1.7 with the community Multiplayer 1.7 port (rwmt PR #1000). Host plus client sessions pass for zombie ticking, combat, special zombies, a full night, synchronized incidents, Zombieland UI input from both peers, the feature clusters, a rejoin during an incident and natural population spawning (`S-Multiplayer-Lockstep`).
- **Scope:** Zombieland 1.7 only. The frozen 1.4 and 1.6 payloads stay incompatible with Multiplayer.
- **Evidence boundary:** This document owns the design rules. Runtime results belong in `TEST_SCENARIOS.md` and `TEST_COVERAGE.md`. The session harness is documented in `scripts/README.md`.

## Rule

`MultiplayerSupport.IsActive` (`Source/MultiplayerSupport.cs`) is true only while a Multiplayer session runs. Without a session, every normal code path runs unchanged.

- If a Multiplayer-safe method costs normal play nothing, use it in one code path for both modes.
- Otherwise keep two code paths. The Multiplayer path may be slower than the normal optimisation. This is the accepted price of playing Multiplayer.

## Effects On Normal Play

A review of every Multiplayer hunk found no gate that leaks into normal play. These one-path changes do reach a game without a session:

| Change | Effect without a session |
| --- | --- |
| Zombies-faction raids spawn on the incident's target map. | Bug fix. Before, they spawned on the viewed map. |
| Wanderer neighbour order uses a hash of cell and pass tick. | Same distribution, different draws. Removes one `System.Random` per flooded cell. |
| `Zombie` has no finalizer. | No gameplay effect. Removes garbage-collector-thread scans. |
| Zombie gear health resets to the def default at difficulty 1 or lower. | Bug fix. Before, a damaged range from an earlier, harder game stayed for the whole process. |
| The Symbiant tick fixes the selection-core state, not the first view. | Same values. A fallback core's move clock can start one tick later. |
| The Thumper dialog edits local copies. | Opening it no longer snaps an off-grid interval to the slider grid. |
| Dev-mode debug actions skip a missing map or pawn. | Dev mode only. |

Every other change is gated by `MultiplayerSupport.IsActive`. The added cost in hot paths is one static `bool` check.

## How Multiplayer Runs The Game

- All peers run the simulation in lockstep. Only synchronized commands may change simulation state.
- Multiplayer ticks each map in its own context: map `Verse.Rand` state, map clock (`TicksGame`) and map faction. `TickManager.MapComponentTick` runs inside that context.
- `Verse.TickManager.DoSingleTick` runs once per world tick in world context. Its vanilla map ticks are skipped. `TickManagerUpdate` runs per frame, and Multiplayer skips its vanilla body.
- Multiplayer isolates `Verse.Rand` calls from UI, drawing, motes, flecks, effecters and sounds. It does not patch `System.Random` or `UnityEngine.Random`. `Verse.Rand` is not thread-safe.
- Desync detection compares only `Verse.Rand` state after each map tick, world tick and command. Other divergence shows up later, through `Rand` use.
- At join points, every peer of a hosted server saves and reloads. This goes through `SavedGameLoaderNow.LoadGameFromSaveFileNow`, without `GameDataSaveLoader.LoadGame` or a scene change. Only saved state and state rebuilt deterministically from the save survive.

## Design Decisions

| Area | Normal play | Multiplayer |
| --- | --- | --- |
| Zombie ticking | `DoSingleTick` postfix ticks all maps. Frame-time saturation control and the camera view choose which zombies tick. | `TickManager.MapComponentTick` ticks the map's zombies. Only the zombie-count budget applies. No camera priority. |
| Incident spawning | Unity coroutine on the camera with a 10 ms frame budget. | Per-map coroutine list in `TickManager`, 50 zombie creation steps per map tick. |
| Wanderer grids | Per-frame flood fill with a real-time slice. | Per-map pass stepped once per map tick, 1000 cells per step. |
| Wanderer random order | Hash of cell and pass tick (one path). | Same. |
| Avoid grid | Background worker thread. | A pass on the map tick floods 2000 cells per tick. Unforced passes start at most once per game second. A forced request during a pass starts the next pass when it ends. |
| `Constants.random` | `System.Random`. | `Verse.Rand` facade. |
| Dangerous-situation letter throttle | Real-time throttle. | 300-tick throttle per map in `TickManager`. Letter stacks are local to each peer. |
| Static state on reload | Reset by `GameDataSaveLoader.LoadGame` and scene changes. | Also reset by the `LoadGameFromSaveFileNow` prefix. |
| Raid map | Incident target map (one path). | Same. |
| UI changes | Run directly. | Synchronized methods in `Source/MultiplayerCommands.cs`. |
| Debug incidents | Run directly. | `TriggerZombieIncident` carries map and mouse cell, so automation can trigger synchronized incidents. |

## Invariants

- Simulation code must not read real time, frame counts, the camera, the selection, `Find.CurrentMap`, local preferences or local mod configuration.
- Simulation code must not use `System.Random` or `UnityEngine.Random`, and must not use `Verse.Rand` off the main thread.
- Background threads must not deliver results that change simulation state while a session runs.
- Finalizers must not touch game state or `Verse.Rand`. They run on the garbage collector's thread at times that differ per peer. `Zombie` has no finalizer for this reason.
- Work that a thread or a frame budget sliced in normal play must run as a tick-stepped coroutine with a fixed work budget per tick. Slice boundaries must depend only on synchronized state, such as cell counts or iterator steps, never on real time, time speed or local caches.
- Static state that influences simulation must reset on every load, including Multiplayer reloads.
- Simulation code must not wait on `LongEventHandler` during a session. A joining client runs its first tick with a queued long event.
- UI code must change simulation state only through a `[SyncMethod]`. A `[SyncMethod]` runs directly when no session is active.
- Interface code must not write caches or schedules that simulation reads. `MultiplayerSupport.InInterface` marks interface calls during a session.
- A region rebuild can run between ticks on one peer only, for example from an interface query. Rebuild hooks must not use the map's `Verse.Rand`. `ZombiePathing.UpdateRegions` seeds its own state during a session.
- Coroutine yield counts must not depend on local configuration. `TickTasks` yields once in every sound branch during a session, because `USE_SOUND` is local.
- Mod metadata loads Zombieland after `rwmt.multiplayer`. The old `incompatibleWith Multiplayer` entry never matched the Multiplayer packageId and was removed.

## Known Limits

- Multiplayer mode uses more main-thread time than normal play: tick-stepped avoid grid and wanderer passes, and no frame-adaptive zombie throttling. Their cost grows with ticks per frame, so high speeds with large hordes run slower than in single player. Measured on the host with 250 zombies at Superfast: wanderer about 0.8 ms per tick at 2000 cells (now 1000), avoid grid about 0.8 ms per 2000-cell step, incident spawning about 1.4 ms per tick at 50 steps. Zombie ticking stays the largest cost, at about 1.1 ms per tick.
- `ZombieGenerator.TryResolveApparelGraphic` depends on the graphic cache, which differs per process. It can change `Verse.Rand` use only for pool apparel that has a missing texture at its real path. None is known in Core and the DLCs.
- A join point reloads every peer and drops transient state equally: a running incident ends early, avoid-grid and wanderer passes restart, and the population spawn counter restarts.
- `CONTAMINATION` selects patches at startup and cannot follow the host. A mismatch shows a warning, and that session desyncs.
- The settings window sends the whole timeline in one command. Two players who edit settings at the same time keep only the last change. A command is limited to 64 KB, so a very long timeline with large blacklists can fail to send.
- Async time: no desync is known. `UpdateGameSettings` interpolates the global settings from each map's clock, and the zombie-free schedule follows the world clock. Maps that run far apart in time therefore share settings and silence windows less precisely.
- Each dying zombie logs "Deep-saving destroyed thing ... saveDestroyedThings==false" on every peer during a session. Multiplayer's `TimestampFixer.FixPawn` serializes the destroyed pawn when it passes to the world. The warning is harmless and does not occur in single player.
- Toggles and sliders send absolute values, as vanilla Multiplayer commands do. If two players change the same control at the same moment, the last command wins.
- Not tested in a session: the Symbiant abandon confirmation (it needs a second colony map), the decontamination quest, multifaction sessions, the arbiter, and peers on different machines or CPU architectures.
- The Multiplayer-Compatibility mod contains a patch for an older Zombieland build. If it is updated for 1.7, it must skip Zombieland versions that ship this mode.
