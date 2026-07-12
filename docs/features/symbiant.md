# Zombie Symbiant Design And Release Contract

## Purpose

The Zombie Symbiant is an indoor colony-room infestation with a linked-host reward. It is not a normal enemy and not a second spitter. The player decision is simple: tolerate disruptive slime in useful rooms to gain host benefits, or spend medical resources to sever the bond and let the remaining slime retreat.

The feature should be legible and annoying in a RimWorld way. It disrupts movement, work, tending, corpse logistics, and host management, but it should not randomly destroy storage, spread disease, or require ordinary combat cleanup.

## Player Loop

- A green side letter announces a Zombie Symbiant in a used indoor room and points to the slime and linked host.
- The Symbiant spreads through used indoor rooms one cell at a time.
- Slime slows pawns crossing it and reduces work/tend speed for affected pawns standing on it.
- The linked host gains benefits as the Symbiant grows: zombie infection immunity from the bond plus random benefits awarded at fixed cell intervals determined when the Symbiant starts.
- Feeding with corpses grows the Symbiant faster. A colonist right-clicks the Symbiant and chooses one available corpse for a one-shot hauling job; humanlike and fresh corpses give larger growth pulses.
- Clean removal is host surgery through `SeverSymbiantSymbiosis`. The operation uses difficulty-scaled zombie extract and industrial medicine through RimWorld's normal bill ingredient path.
- After severance, or after host death, the Symbiant retreats quickly and then disappears.

## Core Invariants

- One active Symbiant per map.
- The authoritative host link lives on `ZombieSymbiant`; `SymbiantSymbiosis` is display/sync state and is recreated when missing.
- The exact host identity persists while that pawn is travelling, contained, or spawned on another map. Bond activity uses the host's effective `MapHeld`: carrying, rescue, arrest, pod loading, and containment inside a holder on the Symbiant's map remain active, while a host with no effective map or a different effective map is dormant. The active-to-dormant transition creates a neutral right-edge letter whose body explains the inactive effects and conditional reactivation rule; the host health tab shows the same warning.
- Host benefits, zombie infection immunity, zombie targeting protection, automatic healing, shared damage, and surgery are same-map effects only.
- Host selection is independent from spawn room selection.
- Natural spawn requires an eligible host and a used indoor room plan.
- Hostless slime is for debug/test or fallback cleanup. It has no host benefits and no host trauma.
- Direct player damage does not remove the Symbiant or make surgery safer.
- Non-gameplay cleanup paths detach the link without host trauma.
- Every non-corpse Symbiant destruction removes the pawn from `WorldPawns` and discards it. `Pawn.Kill` remains the corpse-producing exception and applies the same active-host trauma as other uncontrolled destruction.
- Game finalization removes and safely discards any Symbiant already stranded in `WorldPawns` by an older release, clearing its stale host bond without trauma. This migration is idempotent and also makes the invariant true for upgraded saves.
- If the exact host object is eventually destroyed/discarded and can no longer be resolved, the bond is irrecoverably severed and the Symbiant starts retreat instead of waiting forever.
- Old saves may contain removed legacy defs such as `SymbiantCoagulantPack`; load errors for those removed defs are expected to be non-fatal.

## Settings

The simplified player-facing settings are:

- `symbiantEnabled = true`
- `symbiantMaxCells = 400`

The maximum-size slider allows up to `ZombieSymbiant.MAX_METABALLS = 4000`. Event timing, growth cadence, difficulty scaling, benefit intervals, extract cost, and visual behavior are internal balancing controls.

## Spawn And Room Selection

- Scheduling is event-style and derived from difficulty, zombie threat, colony pressure, and used indoor room pressure.
- Candidate rooms are enclosed, non-huge, non-fogged, proper indoor rooms.
- Home area is a strong signal, but not the only signal. Rooms with recent movement pheromones or valuable colony-use objects also qualify so home-area editing cannot trivially suppress the feature.
- Spawn-room scoring prefers recent traffic and useful objects such as owned beds, worktables, storage, nutrient-paste utility, batteries, coolers, heaters, and similar colony infrastructure.
- If the pheromone grid cannot answer, room and colony-center signals are acceptable fallbacks; randomness is the last resort.

## Host Eligibility

Eligible hosts are spawned, living, free player colonists that are humanlike flesh pawns, adult/non-child by RimWorld category, and suitable for normal colony surgery. The selection rejects prisoners, slaves, guests, temporary joiners, quest lodgers, caravan pawns, shuttle occupants, Save Our Ship holograms, non-flesh optional-mod pawns, existing Symbiant hosts, Zombieland pawns, and late/active zombie infection cases.

If the host has no effective map or is held or spawned on another map, the authoritative link and display hediff persist but all same-map effects turn off. A neutral right-edge letter announces the active-to-dormant transition and keeps the full warning available in the normal letter body; the hediff description replaces its active benefit summary with the same dormant warning. A same-map holder does not interrupt the bond or generate dormancy feedback. If the same pawn later shares the Symbiant's map again, the existing bond reactivates; another host is never chosen as a travel fallback, and sold or kidnapped hosts are not promised a return. Host death anywhere ends the bond and starts retreat. Destroying the Symbiant or abandoning its map while the host is away removes the remote hediff without harming that host.

The Symbiant itself never boards an Odyssey gravship. Its def sets `bringAlongOnGravship=false`, so a departure that preserves the origin map leaves the Symbiant there and the travelling host dormant. If the gravship launch abandons the origin map, Zombieland destroys and discards the map-bound Symbiant before vanilla snapshots and transfers spawned pawns to `WorldPawns`; that clears the remote host link safely instead of creating an orphaned world-pawn Symbiant.

Automatic and direct map removal uses the same safety rule. Before `Game.DeinitAndRemoveMap` can enter `MapDeiniter` and pass remaining map pawns to `WorldPawns`, Zombieland safely severs, destroys, and discards every map-bound Symbiant, including one held inside another map object. A linked host still spawned on the closing map is not killed; vanilla may transfer that host to `WorldPawns` normally. This covers temporary sites, camps, settlements, ordinary pocket maps, space maps, and other vanilla callers that close a map without invoking `MapParent.Abandon`.

The Symbiant cannot be extracted as a travelling or contained pawn. Any ordinary non-relocation despawn safely severs, destroys, removes from `WorldPawns`, and discards it, while the internal room-reseed path explicitly marks its temporary despawn and respawn. The hook stands down during `Pawn.Kill` so vanilla can create and finalize a valid Symbiant corpse; an uncontrolled kill collapses an active linked host first, while the suppressed in-progress corpse edge is cleaned safely if the host later dies. Anomaly's `CloseMetalHell` prefix mirrors vanilla's `MapHeld?.IsPocketMap` boundary before doing cleanup: a real pocket-map close removes the Symbiant before vanilla can add it to `metalHellPawns`, while an ordinary-map or unheld pawn remains a no-op. The despawn hook remains a fallback for other extraction paths.

## Spread, Relocation, And Retreat

- One growth pulse adds one room or door cell, or does nothing if no valid target exists.
- Spread prefers open cells before wall targets.
- Spread never continues outdoors.
- Closed and open doors are valid spread cells and remain door objects.
- Natural rock and non-constructed blockers are not breached.
- Constructed-wall breach behavior is intentionally conservative and must only target valid indoor continuation.
- At `symbiantMaxCells`, expansion stops but the Symbiant remains active.
- When a cell is removed, contamination on that cell is cleared once.

Relocation handles deconstruction, battle damage, and messy rebuilding:

- Visible cells that stop counting as integrated indoor slime become relocation material.
- If the linked root loses all integrated indoor cells, a grace window lets temporary room openings settle.
- While uprooted during the grace window, ordinary growth and relocation pulses are paused.
- If another used indoor room exists after the grace window, the root reseeds there as one visible cell and carries the old footprint as relocation debt.
- Relocation debt is repaid one cell at a time at double the current adaptive growth speed.
- If no used indoor rooms exist, the Symbiant remains dormant and does not grow outdoors.
- If all valid indoor targets are exhausted or blocked, inspect text reports the contained state.
- Severed, dead-host, or hostless cleanup retreat removes one cell per hour until the Symbiant disappears.
- A save made after the final cell starts its outgoing animation preserves the pending-retirement state. A paused reload may display the zero-cell shell until game time resumes; the first lifecycle tick destroys and discards it outside the draw pass.

## Benefits And Disruption

The current bond factor and host aura scale from integrated visible cells:

- full credit for cells in roofed, proper colony rooms with recent traffic or valuable colony use,
- partial credit for door cells or lower-confidence useful cells,
- no credit for unroofed, outdoor, fogged, huge, improper, or invalid cells.

`fullBenefitCells = clamp(ceil(eligibleColonyRoomCells * 0.20), 20, symbiantMaxCells)`.

`benefitFactor = clamp01(integratedVisibleCells / fullBenefitCells)`.

The host starts with zombie infection immunity from the bond. Additional random benefits are awarded in acquisition order at fixed total-cell intervals determined when the Symbiant starts. Current benefit types are:

- mood fixed at 50%,
- no food or rest need,
- all skills, stackable. Each stack grants +4 below 200% Zombieland difficulty, +3 from 200% to below 300%, +2 from 300% to below 400%, and +1 at 400% or above. Enabled Bio skill rows show the level without the Symbiant plus the actually applied bonus (for example `10 + 4` or the capped `18 + 2`), while the bar and gameplay use the combined total. The skill tooltip adds one Symbiant-benefit line and identifies any bonus lost to the level-20 cap,
- Moving capacity +25%, stackable. This is a capacity-layer benefit: the health tab, the colonist info dialog's Move Speed stat, real pathing, and vanilla movement-dependent stats such as melee dodge and hunting stealth all consume the same value,
- Manipulation capacity +25%, stackable. Health and every vanilla stat or action that consumes Manipulation use the increased value,
- zombie targeting protection,
- automatic healing, stackable.

The acquired benefit list should be visible on the host hediff tooltip and on the Symbiant info/inspect surface. A dormant Symbiant keeps its selection-panel inspect text compact by adding only the dormant-bond status to the normal linked-host summary; the full dormant explanation belongs in the `(i)` info dialog.
When Moving or Manipulation stacks are active, the host hediff keeps its `Benefits:` list and adds `Combined:` immediately before RimWorld's aggregate capacity rows.

Display-hediff synchronization is idempotent: missing state is recreated and duplicate/corrupt `SymbiantSymbiosis` entries collapse to one entry tied to the authoritative Symbiant ID. Infection immunity is applied when a bite hediff is added, not one tick later. Disabled skills remain at vanilla's disabled value rather than receiving the difficulty-scaled skill patch. The Bio breakdown treats the result of RimWorld and other mods' skill-level logic as the base, suppressing only Zombieland's Symbiant addition while calculating the displayed components.

The shared-health pool remains on the Symbiant while the bond is dormant, but damage never crosses maps. Damage to the separated host does not drain the pool; damage to the Symbiant does not leak to or kill the separated host. If the pool fails while the host is away, the Symbiant is removed and the remote host survives.

Pawn disruption remains non-lethal:

- Pawns standing on slime have reduced medical tend speed and work speed unless exempt.
- Pawns crossing slime pay difficulty-scaled movement slowdown; current scaling is 10% at difficulty 1 to 50% at difficulty 5.
- The same-map host is exempt from the negative slime effects.
- Footstep splash feedback is movement-entry feedback, not a tick effect.

## Feeding And Surgery

- Feeding consumes one valid non-Zombieland corpse and adds growth pulses.
- Feeding is a one-shot float-menu order tied to the selected corpse category. There is no persisted continuous-feed request and no autonomous Hauling workgiver mode; old `feedRequested` save data is ignored.
- Humanlike corpses add 2 cells, non-humanlike corpses add 1 cell, and fresh corpses add 1 more cell.
- The bond permits surgery while the linked host has the same effective map as the Symbiant; RimWorld still requires normal physical access to the pawn before a doctor can perform the operation.
- Surgery consumes difficulty-scaled zombie extract plus industrial medicine through RimWorld's normal ingredient availability path.
- Successful surgery removes the link without host trauma; the unbound Symbiant retreats cell by cell.
- The recipe worker must not manually consume extra extract outside RimWorld's bill ingredient system.

## Runtime Shape

`ZombieSymbiant : Pawn` is an implementation shell. Semantically it is room-scale slime with a custom renderer, cell set, host link, feed interaction, positional disruption, and path cost.

The pawn shell is deliberately isolated from normal pawn/combat systems:

- not a fighter,
- zero combat power,
- hidden from `map.mapPawns`,
- discovered through `ZombieSymbiant.ActiveSymbiant(map)` and `listerThings`,
- selectable by clicking anywhere inside its custom selector rect,
- skipped by ordinary attack targeting, story danger, fleeing, predation, auto-attack, and explicit attack jobs,
- no normal pawn inspect tabs while selected, so Mood, Gear, Health, Combat Log, and similar pawn-tab surfaces do not treat it as an ordinary pawn,
- no selected status/dashboard gizmo in the simplified design,
- restricted to the Symbiant job plus inert fallback jobs.

The long-term cleaner type would be a custom `Thing`/`ThingWithComps`, but that migration is separate from the v1 gameplay surface.

## Rendering And Performance

- Gameplay default cap is 400 cells.
- Technical stress ceiling is `ZombieSymbiant.MAX_METABALLS = 4000`.
- The CPU feeds cell coordinates, centers, radius, and radius-scale data to GPU resources.
- Metaballs are rendered by the GPU shader path; do not move blob rasterization to CPU code.
- Cell in/out animation changes center and radius scale over roughly one second at 1x speed.
- Growth radius eases in; shrink radius eases out.
- Texture/material/buffer resources are transient and must be released on despawn, destroy, map removal, load, shutdown, and main-menu transitions.
- Hot paths must reject unrelated calls before active-Symbiant lookup. Do not scan `map.mapPawns.AllPawns` from Symbiant hot paths.
- Active-Symbiant caches are map-object keyed and transient. They are cleared on load, shutdown, main-menu transitions, and map removal.

Performance evidence from the implementation pass showed default 400-cell and diagnostic 4000-cell Symbiants close to same-session no-Symbiant baselines after cache, rendering, and hot-path fixes. Rerun those stress checks when touching rendering, path cost, cell stat effects, Symbiant ticking, host hediff sync, or active-cache behavior.

## Validation Ownership

This document owns intended behavior and release gates, not chronological test results:

- `TEST_COVERAGE.md` owns durable runtime evidence and operation IDs.
- `TEST_SCENARIOS.md`, under `S-Symbiant-Symbiosis`, owns the reusable scenario and release-check matrix.
- `coverage/ZL_COVERAGE_INDEX.tsv`, row `C.SPECIAL.SYMBIANT_INFESTATION`, owns current coverage state and remaining gaps.
- `TODO.md` owns active defects and explicitly deferred implementation work.

Rerun the relevant scenario or bridge contract after changing a behavior surface; do not copy another PASS snapshot into this document.

## Release Gate

For a release candidate touching Symbiant behavior:

- `scripts/build-quiet.sh`
- XML validation for `1.6/Defs`, `1.6/Patches`, and `1.6/Languages`
- translation placeholder/key parity if language files changed
- stale-key scan for removed Symbiant settings and old benefit names
- one loaded-game smoke that spawns or observes a Symbiant
- cross-map host-link, shared-damage, and return checks when host lifecycle behavior changed
- clean warning-or-higher logs except documented old-save missing-def compatibility errors
- asset bundle rebuild only if shader/material assets changed
- tracked DLLs restored before a normal source-only commit
