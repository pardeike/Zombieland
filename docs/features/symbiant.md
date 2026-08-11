# Zombie Symbiant Design And Release Contract

## Purpose

The Zombie Symbiant is an indoor colony-room infestation with a linked-host reward. It is not a normal enemy and not a second spitter. The player decision is simple: tolerate disruptive slime in useful rooms to gain host benefits, or spend medical resources to sever the bond and let the remaining slime retreat.

The feature should be legible and annoying in a RimWorld way. It disrupts movement, work, tending, corpse logistics, and host management, but it should not randomly destroy storage, spread disease, or require ordinary combat cleanup.

## Player Loop

- A green side letter announces a Zombie Symbiant in a used indoor room and points to the slime and linked host.
- The Symbiant spreads through used indoor rooms one cell at a time.
- Slime slows pawns crossing it and reduces work/tend speed for affected pawns standing on it.
- The linked host gains benefits as the Symbiant grows: zombie infection immunity from the bond plus random benefits awarded at fixed cell intervals determined when the Symbiant starts.
- Feeding with corpses grows the Symbiant faster. A drafted or undrafted colonist right-clicks the Symbiant and chooses an eligible corpse for a one-shot hauling job; interchangeable animal corpses share one row, while human and other non-animal corpses remain individual choices.
- Clean removal is host surgery through `SeverSymbiantSymbiosis`. The operation uses difficulty-scaled zombie extract and industrial-or-better medicine through RimWorld's normal bill ingredient path.
- After severance, or after host death, the Symbiant retreats quickly and then disappears.

## Core Invariants

- One active Symbiant per map.
- The authoritative host link and benefit state live on `ZombieSymbiant`; `SymbiantSymbiosis` is an inert display/sync marker and is recreated when missing. Its severity is fixed at `0.001`, bounded to that value, explicitly non-lethal, and contributes no health summary loss, pain, bleeding, or tendable/mergeable condition state.
- The exact host identity persists while that pawn is travelling, contained, or spawned on another map. Bond activity uses the host's effective `MapHeld`: carrying, rescue, arrest, pod loading, and containment inside a holder on the Symbiant's map remain active, while a host with no effective map or a different effective map is dormant. The active-to-dormant transition creates a neutral right-edge letter whose body explains the inactive effects and conditional reactivation rule; the host health tab shows the same warning.
- Host benefits, zombie infection immunity, zombie targeting protection, automatic healing, shared damage, and surgery are same-map effects only.
- Host selection is independent from spawn room selection.
- Natural spawn requires an eligible host and a used indoor room plan.
- Hostless slime is for debug/test or fallback cleanup. It has no host benefits and no host trauma.
- Direct player damage does not remove the Symbiant or make surgery safer.
- Ordinary inspection has one visible pulsing core cell. Clicking other slime cells passes through to the cell's ordinary contents, but generic targeting and hostile combat still treat every occupied slime cell as part of the attackable organism.
- Ordinary hostile humanlike and mechanoid pawns may choose the Symbiant as a colony target. The Symbiant receives no artificial target-priority bonus; selection uses the same distance, line-of-sight, range, cover, and friendly-fire considerations as an ordinary target, evaluated against its exposed slime cells.
- Damage aimed at the Symbiant runs through the real vanilla or modded damage worker, then drains only the custom shared-health pool by the worker's actual post-armor damage. It never creates a real injury on the host.
- Plain anatomical wounds are removed from the Symbiant after their damage worker has completed. Fire, stun, additional hediffs, custom injury subclasses, and unknown modded condition comps remain free to run, while part health, pain, capacities, downing, death, and summary health remain owned by the shared pool.
- While the bond is active, the host Health tab shows at most seven named grey damage-history rows such as `Crack: 40 damage`, plus one compact `Other` row. The surrounding `Symbiant bond` group and detailed tooltip carry the echo explanation without repeating it in every row. These whole-body rows are inert and cumulative for the life of the bond. Dormancy physically removes them; reunion reconstructs them from the Symbiant's persisted ledger.
- Non-gameplay cleanup paths detach the link without host trauma.
- Every non-corpse Symbiant destruction removes the pawn from `WorldPawns` and discards it. `Pawn.Kill` remains the corpse-producing exception, but both paths safely detach the host. The bond may kill its active host only through the explicit shared-health-exhaustion transition; generic `Kill`, `Destroy`, despawn, map cleanup, and migration paths cannot authorize host death.
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

The Symbiant cannot be extracted as a travelling or contained pawn. Any ordinary non-relocation despawn safely severs, destroys, removes from `WorldPawns`, and discards it, while the internal room-reseed path explicitly marks its temporary despawn and respawn. The hook stands down during `Pawn.Kill` so vanilla can create and finalize a valid Symbiant corpse; that kill safely detaches the living host, while the suppressed in-progress corpse edge is cleaned safely if the host later dies. Anomaly's `CloseMetalHell` prefix mirrors vanilla's `MapHeld?.IsPocketMap` boundary before doing cleanup: a real pocket-map close removes the Symbiant before vanilla can add it to `metalHellPawns`, while an ordinary-map or unheld pawn remains a no-op. The despawn hook remains a fallback for other extraction paths.

## Spread, Relocation, And Retreat

- One growth pulse adds one logical cell or does nothing. All automatic growth, feeding, movement, relocation debt, construction repair, and developer/Bridge actions pass through the same footprint-mutation gate, so pending construction repair blocks every other footprint mutation and pending invalid-cell or room-connectivity repair blocks net growth.
- Placement uses one canonical classifier. A valid indoor floor cell is in bounds, unfogged, roofed, walkable, and in a proper non-huge room that does not use outdoor temperature. A valid door cell is roofed and cardinally adjacent to at least one such room. Genuine exterior cells must have outdoor-temperature or map-edge semantics; a huge roofed hall, enclosed cavern, isolated roof hole, fogged cell, blocker, or otherwise ineligible interior is not exterior.
- Relevant indoor rooms are the current union of colony-use candidate rooms and rooms already occupied by this Symbiant. Unused ruins do not prevent a full-base overflow decision, while an occupied room cannot disappear from the capacity calculation merely because its colony-use signals change.
- A Symbiant may own several globally disconnected patches, but each eligible indoor room converges to at most one cardinally connected patch. The persisted establishment anchor, not the movable inspection core, owns the 25% establishment threshold for its current room.
- Before the anchor room reaches 25% of its valid floor capacity, normal growth remains in that room when placement is available. Afterward, an unoccupied relevant room is founded directly on one valid cell, preferring a room separated from an occupied patch by one door or divider boundary and otherwise choosing the best remote relevant room. Founding never destroys a divider wall and does not require an artificial path through intervening cells.
- Direct founding snaps the establishment anchor and inspection core to the new occupied cell without animating either through walls. Once every relevant room has a patch, growth resumes across occupied rooms and may fill all remaining valid indoor floor and door targets.
- Closed and open doors are ordinary valid targets and remain door objects. A free legal door prevents the indoor-capacity state from being considered full.
- Growth, ambient movement, and relocation share the same soft location preference: recent colony traffic remains strongest, neighboring slime favors compact shapes, and beds, dining tables, worktables, and storage cells are avoided when practical. Bare floor is required only while a bare founding cell exists; an otherwise valid empty room whose floor is entirely covered by passable furniture can be founded on the best furnished cell without removing the furniture. Ambient movement stays within the source cell's indoor/exterior domain, admits an eligible-room target only when that room already contains an established patch, cannot found another room patch, and cannot substitute for metered exterior-to-interior relocation. Removing an indoor movement source must keep that source room's patch cardinally connected even when another occupied room supplies a global route around the gap. An exterior move must leave the old footprint intact and give the destination a surviving cardinal neighbor other than the moved source, so an outdoor leaf cannot step outward and detach itself.
- The last 16 changed coordinates receive a temporary soft anti-reversal adjustment. This bounded in-memory history is not saved, carries no correctness state, and cannot block the only valid target.
- Same-room connectivity is validated before the first footprint decision and after topology changes. A component containing valid indoor floor or door biomass is retained ahead of every wholly invalid component; size is compared next, with root/core position used only as later tie-breakers. If no component is currently valid, the largest component remains the fallback. Cells in secondary components enter one persisted, deduplicated migration queue with a transient hash lookup. One ordinary movement pulse may also hard-relocate one queued cell beside the established component, with no repair animation. If another move connects a queued component cardinally, that whole component retires from the queue without needless per-cell migration.
- Room, roof, and construction notifications only mark placement work stale and wake the slow repair cadence; they never enumerate rooms in the callback. Multiple notifications coalesce, and the next topology-safe mutation boundary rebuilds the queue even if an older queue was blocked. Load normalization prunes missing, duplicate, out-of-bounds, and no-longer-relevant entries while keeping the list and hash lookup synchronized.
- Exterior overflow is exceptional full-base behavior. It can begin only after a fresh exact audit finds at least one relevant room, every relevant floor and door target full, no pending repair or stale topology work, the footprint under the configured maximum, and no unauthorized exterior/invalid cells already needing repair.
- The first exterior cell prefers an already open cardinal route through an occupied perimeter door. Only when no such route exists may one player-owned constructed perimeter wall be removed. The wall commit rejects natural rock, mineables, non-player walls, doors, non-wall edifices, fog, map edges, multiple-wall tunnels, and every wall whose removal could connect any eligible indoor rooms. A divider wall is therefore never an exterior transition.
- A wall breach is committed as one validated operation: exactly one wall is removed, the former wall cell is added deterministically, and exterior authorization is set only after that logical cell exists. The connected source-side patch is carried into that authorization when the breached room becomes exterior after topology rebuild, so deferred feed pulses can continue. Authorization is persisted on the approved exterior cells themselves, so later roof or wall damage cannot extend it to a separate exterior component. No second wall may be breached during the same overflow episode. Once that component exists, every continuation candidate and commit must come from and touch its authorized exterior cells; indoor cells and other perimeter doors cannot start another outdoor front.
- Fresh indoor placement always stops exterior growth. One exterior or otherwise invalid cell then relocates indoors per relocation pulse until no movable repair cell or no indoor placement remains. Authorization clears synchronously when the final exterior cell is removed or reclassified; if indoor capacity fills again while authorized exterior cells remain, cardinal exterior growth may resume without another breach.
- Exterior cells stay visible, disruptive, attackable, and count toward `symbiantMaxCells`, but grant no integration credit, new benefit progress, shared-health capacity, or host-facing protection. Arbitrary damage or roof exposure never authorizes exterior growth: exposed cells relocate at the normal repair cadence when relevant indoor placement exists, while the no-room case retains grace, reseed, and dormancy behavior.
- Lowering `symbiantMaxCells` below the current footprint or footprint plus relocation debt never deletes cells or debt. It stops new growth and breaches; count-preserving repair remains legal and debt waits for capacity.
- At `symbiantMaxCells`, expansion stops but the Symbiant remains active. When a cell is removed, contamination on that cell is cleared once.
- The inspection core remains a one-cell UI affordance. It follows a moved cell, hands off to a surviving low-clutter cell before removal, snaps safely when a repair/founding move crosses distance, and after roughly six in-game hours may ride the next eligible ambient cell move so it does not remain fixed forever. During an outgoing handoff, the removed source remains the selector, tooltip, and context-menu hit cell only while the rendered knot still rounds to it; a feeding job created there binds to the occupied destination cell.

Relocation and construction repair handle deconstruction, battle damage, roof changes, and messy rebuilding:

- Invalid and unauthorized exterior cells relocate at the faster relocation cadence without waiting for the 25% establishment rule. Empty relevant rooms receive one connected seed before relocation adds second cells, after which projected room coverage keeps the recovered footprint distributed. A relocation into an occupied room must join that room's established patch.
- If the canonical pawn cell becomes invalid while another logical cell remains usable, the implementation rebases the pawn identity to a valid survivor without changing any unrelated absolute cell, migration entry, motion, host link, or serialized authorization.
- Any impassable `Building.SpawnSetup` whose occupied rectangle intersects the logical footprint schedules one deferred atomic repair, including player construction, loading, hostile/quest spawns, and official-DLC buildings. The hook rejects unrelated buildings after an active-map and bounds check and does not scan the whole footprint when the rectangles do not intersect.
- A construction batch revalidates all pending building footprints after room topology settles, excludes every pending footprint and already reserved target, and plans indoor destinations once for the batch. Covered cells are processed root first, then inspection core, then other cells. Each is hard-relocated to a legal connected indoor destination when possible or crushed with no relocation debt; motion and migration state touching repaired coordinates is cancelled. The completed building is never removed.
- Vanilla unwalkable-pawn recovery is suppressed for a covered Symbiant root so it cannot teleport the canonical `Position` without rebasing relative cells. If safe repair cannot yet run, the root remains owned by the deferred repair; path callers receive failure until the root is valid. If the final logical cell is crushed, the Symbiant is removed and its living host survives unlinked.
- If all integrated indoor cells disappear while another relevant room has legal placement, no uprooting grace starts: even a one-cell canonical root hard-relocates indoors at the normal repair cadence, preserving cell count and creating no debt. The four-hour grace applies only while no positive-capacity relevant room exists, allowing temporary openings to settle. This recovery boundary also applies after legitimate exterior overflow: authorization remains historical state, not permission to treat a map with no relevant rooms as a full base, and further outdoor growth stops. Any pending relocation debt remains unchanged while the no-room transition waits for the reseed check; it cannot add another authorized outdoor cell first. Afterward, the linked host's otherwise eligible room may receive a one-cell root reseed and the lost visible footprint becomes relocation debt; if no such room exists, the Symbiant remains dormant. Room/roof notifications wake the check without scanning in the callback; no-room polling checks its deadline before placement metrics or capacity are rescanned, using the repair cadence during grace and the blocked retry cadence after dormancy.
- Relocation debt is repaid one cell at a time at double the current adaptive growth speed using the same multi-room distribution and maximum-cell rules. When a repayment founds an empty room, the establishment anchor and inspection core snap to that first cell exactly as they do during ordinary founding. Construction-crushed cells never create debt.
- If indoor targets exist but are currently blocked, inspect text reports the contained state and both new exterior growth and inward relocation pause. Severed, dead-host, or hostless cleanup retreat removes one cell per hour until the Symbiant disappears.
- A save made with queued migration, active exterior authorization, pending construction repair, or a final outgoing cell preserves the required correctness state. Post-load normalization and construction validation run on the first topology-safe game tick, not while `ExposeData` is rebuilding the map. A paused reload may display the zero-cell shell until game time resumes; the first lifecycle tick destroys and discards it outside the draw pass.

## Benefits And Disruption

The current bond factor and host aura scale from integrated visible cells:

- full credit for cells in roofed, proper colony rooms with recent traffic or valuable colony use,
- partial credit for door cells or lower-confidence useful cells,
- no credit for unroofed, outdoor, fogged, huge, improper, or invalid cells.

`fullBenefitCells = clamp(ceil(eligibleColonyRoomCells * 0.20), 20, symbiantMaxCells)`.

`benefitFactor = clamp01(integratedVisibleCells / fullBenefitCells)`.

The host starts with zombie infection immunity from the bond. Additional random benefits are awarded in acquisition order at fixed host-effect-cell intervals determined when the Symbiant starts. Only valid indoor floor and door biomass advances these thresholds; exterior and invalid cells do not. The interval scales from 20 cells at 100% Zombieland difficulty to 50 cells at 500% difficulty. Shared-health capacity and damage leakage use the same indoor/door host-effect count, so exterior overflow does not strengthen the host-facing shield. Current benefit types are:

- mood fixed at 50%,
- no food or rest need,
- all skills, stackable. Each stack grants +4 below 200% Zombieland difficulty, +3 from 200% to below 300%, +2 from 300% to below 400%, and +1 at 400% or above. Enabled Bio skill rows show the level without the Symbiant plus the actually applied bonus (for example `10 + 4` or the capped `18 + 2`), while the bar and gameplay use the combined total. The skill tooltip adds one Symbiant-benefit line and identifies any bonus lost to the level-20 cap,
- Moving capacity +25%, stackable. This is a capacity-layer benefit: the health tab, the colonist info dialog's Move Speed stat, real pathing, and vanilla movement-dependent stats such as melee dodge and hunting stealth all consume the same value,
- Manipulation capacity +25%, stackable. Health and every vanilla stat or action that consumes Manipulation use the increased value,
- zombie targeting protection,
- automatic healing, stackable.

The acquired benefit list should be visible on the host hediff tooltip and on the Symbiant info/inspect surface. A dormant Symbiant keeps its selection-panel inspect text compact by adding only the dormant-bond status to the normal linked-host summary; the full dormant explanation belongs in the `(i)` info dialog.
When Moving or Manipulation stacks are active, the host hediff keeps its `Benefits:` list and adds `Combined:` immediately before RimWorld's aggregate capacity rows.

Display-hediff synchronization is idempotent: missing state is recreated and duplicate/corrupt `SymbiantSymbiosis` entries collapse to one entry tied to the authoritative Symbiant ID. The marker does not encode growth or benefit factor in RimWorld's health `Severity`; benefits are read from the linked Symbiant. Adding, removing, or ticking unrelated host hediffs may run RimWorld's normal health-state checks, but never crosses the coupling boundary or changes shared health merely because a state check occurred. Infection immunity is applied when a bite hediff is added, not one tick later. Disabled skills remain at vanilla's disabled value rather than receiving the difficulty-scaled skill patch. The Bio breakdown treats the result of RimWorld and other mods' skill-level logic as the base, suppressing only Zombieland's Symbiant addition while calculating the displayed components.

An external host death remains owned by RimWorld or the initiating mod. Zombieland captures the linked Symbiant without changing health state, lets the original `Pawn.Kill` finish once, and only then severs the authoritative link and removes its display records. Link authority is cleared before any marker hediff is removed. This prevents hediff/capacity reevaluation from recursively entering death handling before RimWorld has installed its own `isBeingKilled` guard. Intentional vanilla outcomes such as maternal death after childbirth are therefore neither suppressed nor converted into a coupling death: the host gets one vanilla death lifecycle, one funeral obligation/letter when eligible, and the surviving Symbiant begins retreat once.

The shared-health pool remains on the Symbiant while the bond is dormant, but damage never crosses maps. Damage to the separated host does not drain the pool; damage to the Symbiant does not injure or kill the separated host. Its damage ledger stays on the Symbiant, while the dormant host contains zero echo objects. If the pool fails while the host is away, the Symbiant is removed and the remote host survives.

Damage aimed directly at the same-map host retains the existing sharing direction for injury-producing damage: the full incoming amount drains the shared pool first, then only the size-scaled leak proceeds through the host's normal armor and injury path. That genuine host injury remains an ordinary white injury. The boundary classifies packets by behavior, not by the overloaded `DamageDef.harmsHealth` flag or numeric amount. A `DamageWorker_AddInjury` worker or subclass shares by default; a `SymbiantSharedHealthDamageExtension` can explicitly opt a custom def in or out. Unknown custom workers fail closed: their native effect still runs on the host, but the packet cannot consume shared health or cause a bond death until explicitly classified. This admits modded injury workers even when their def says `harmsHealth=false`, while rejecting effect-only defs that say `harmsHealth=true`, such as Zombieland's `SeismicWave`. Firefoam, stun, EMP, smoke, pregnancy, anesthesia, and other ordinary hediff/control transitions therefore remain local to their target. A worker may deliberately emit a second packet; each nested packet is classified independently, so a real `DamageWorker_AddInjury` follow-up is shared even when its outer effect packet is not. Damage aimed at the Symbiant is different: vanilla or modded workers run first, actual post-worker `totalDamageDealt` drains the pool once, and the host receives only an inert grey history echo. Zero-health-damage stun or EMP may affect the Symbiant locally but drains no pool and creates no echo. Shared-pool exhaustion is the sole lethal coupling transition; administrative `Pawn.Kill` and `Destroy` safely detach the host.

Shared health recovers without colony work. Any successful pool drain resets the clock. After one full quiet hour, the Symbiant recovers 5% of its currently missing shared health per in-game hour, with at least one point restored per pulse and a final clamp to full. This works while the exact host is dormant on another map, but stops after severance or host loss. Recovery is independent of growth, feeding, relocation, and maximum cell count, so movement cannot become a healing exploit and a fully grown Symbiant can still recover.

Pawn disruption remains non-lethal:

- Pawns standing on slime have reduced medical tend speed and work speed unless exempt.
- Pawns crossing slime pay difficulty-scaled movement slowdown; current scaling is 10% at difficulty 1 to 50% at difficulty 5.
- The same-map host is exempt from the negative slime effects.
- Footstep splash feedback is movement-entry feedback, not a tick effect.

## Feeding And Surgery

- Feeding consumes one valid organic non-Zombieland corpse and adds growth pulses. Mechanoid and other non-flesh corpses are not valid feed.
- The float menu is available to both drafted and undrafted selected colonists and considers every spawned corpse that is unforbidden, inside that colonist's allowed area, reservable, and reachable as a player-forced order. Eligible animals of the same race and freshness share one row that targets the nearest matching corpse; human and other non-animal corpses remain individual rows. The clicked visible core cell is captured as the order's reach, route, wait, and progress target even when it differs from the hidden canonical root. Each is a one-shot order; there is no persisted continuous-feed request and no autonomous Hauling workgiver mode, and old `feedRequested` save data is ignored.
- Humanlike corpses add 2 cells, non-humanlike corpses add 1 cell, and fresh corpses add 1 more cell.
- If the first pulse of a multi-cell feed breaches an exterior wall, the breach cell is added immediately and the remaining persisted pulses resume after room topology settles.
- The bond permits surgery while the linked host has the same effective map as the Symbiant; RimWorld still requires normal physical access to the pawn before a doctor can perform the operation.
- Surgery consumes difficulty-scaled zombie extract plus industrial-or-better medicine through RimWorld's normal ingredient availability path. Herbal medicine is below the required tier.
- Successful surgery removes the link without host trauma; the unbound Symbiant retreats cell by cell.
- The recipe worker must not manually consume extra extract outside RimWorld's bill ingredient system.

## Runtime Shape

`ZombieSymbiant : Pawn` is an implementation shell. Semantically it is room-scale slime with a custom renderer, cell set, host link, feed interaction, positional disruption, and path cost.

The pawn shell is deliberately narrow in normal pawn/combat systems:

- not a fighter,
- zero combat power,
- registered once in normal map pawn and attack-target systems,
- discovered through `ZombieSymbiant.ActiveSymbiant(map)` and `listerThings`,
- inspectable through one visible pulsing core cell; the rest of the slime click-throughs to ordinary cell contents,
- targetable by ordinary hostile humanlike and mechanoid attackers, while player/friendly pawns, animals, turrets, zombies, and Anomaly-specific hostility overrides retain their prior exclusions,
- skipped by story danger, fleeing, predation, and unrelated explicit attack jobs,
- no normal pawn inspect tabs while selected, so Mood, Gear, Health, Combat Log, and similar pawn-tab surfaces do not treat it as an ordinary pawn,
- no player-facing status/dashboard gizmo in the simplified design; when both developer mode and god mode are enabled, the selected Symbiant instead exposes `DEV: Add Cell`, `DEV: Remove Cell`, `DEV: Move Symbiant`, and `DEV: Assign/Unassign` test commands,
- restricted to the Symbiant job plus inert fallback jobs.

The long-term cleaner type would be a custom `Thing`/`ThingWithComps`, but that migration is separate from the v1 gameplay surface.

Combat keeps one real `ZombieSymbiant` Pawn and never moves its canonical `Position` to impersonate another slime cell. Only the root cell is registered in `ThingGrid`; logical cells are supplied through a transient geometry cache with shape-version invalidation. Ranged attacks bind each `Verb` to one deterministic exposed cell, and the same cell is reused for vanilla target-scan gates, weighted target selection, distance/cover/blast-friendly-fire scoring, LOS/range, projectile destination, and impact. Vanilla roof interception/collapse runs before a logical owner impact. Melee jobs keep the real Symbiant in target A, store the reachable stand cell in B, and store the attacked slime cell in C; B/C are rebound if the blob changes shape. Damage always goes to the real Pawn, through the real damage worker, and then into its shared-health pool; attacking a cell never deletes that cell. An explosion overlapping several slime cells damages the organism at most once, using the first affected logical cell for falloff. Combat Extended support is late-bound and fail-open: because CE projectiles derive from `ThingWithComps` independently of vanilla `Projectile`, the adapter reflects CE target/position state and supplies the logical owner to CE's ordinary ballistic, final-impact, and instant-ray collision enumerations/bounds without mutating `ThingGrid` or hard-referencing CE.

The one-cell inspection surface is deliberately separate from that combat geometry. For ordinary inspection and right-click interaction, `GenUI.ThingsUnderMouse` exposes only the current visible core, including the still-rendered outgoing core cell during its short movement handoff. While RimWorld's manual `Targeter` is active, every logical slime cell is exposed so drafted attacks and pawn-targeting abilities can use the whole rendered body. Every non-core logical cell otherwise, and every empty gap in the rectangular draw bounds always, clicks through to the ordinary map cell and its contents. Feeding is likewise offered only from the visible core, and the feeding job carries the corpse to that captured core cell rather than pathing to the hidden canonical root. Enemy AI, verb binding, melee reach, projectile impact, and explosions never consult the inspection core.

The developer widgets call the same one-pulse growth, shrink, and cell-movement paths used by the organism instead of installing map-click debug tools. Assignment lists only currently eligible free colonists; using the same widget on an assigned Symbiant removes the link and its host hediff. These controls are absent unless `DebugSettings.ShowDevGizmos` is true, which in RimWorld 1.6 requires both developer mode and god mode.

## Rendering And Performance

- Gameplay default cap is 400 cells.
- Technical stress ceiling is `ZombieSymbiant.MAX_METABALLS = 4000`.
- The CPU feeds cell coordinates, centers, radius, and radius-scale data to GPU resources.
- Metaballs are rendered by the GPU shader path; do not move blob rasterization to CPU code. Each globally disconnected patch owns a tight render mask and mesh so a remote room seed does not allocate or draw one map-spanning texture.
- Cell in/out animation changes center and radius scale over roughly one second at 1x speed.
- The inspection core is a 0.93-cell organic knot drawn above one occupied cell with RimWorld's alpha-blended transparent shader. Its light-green outer mask uses a broad smooth feather, its swirl rotates slowly counter-clockwise, and its idle pulse eases smoothly into and out of the stronger discovery, hover, and selected states even while the game is paused. A localized hover tooltip teaches the interaction, cell movement uses a smooth handoff whose one-cell hit target follows the rendered knot throughout the move, and selection gradually brightens the whole blob. The core remains visibly inside the underlying slime footprint even when the Symbiant has only one cell, and it is prepared and drawn independently when compute-shader metaballs fall back to the ordinary pawn graphic.
- The core tooltip is registered only over unobscured map input. RimWorld windows and inspect panes, the bottom main-button strip, and the active alert stack take precedence even when their screen coordinates project onto the core's map cell.
- A preferred ambient core move considers only the normal 12-target pool and tests connectivity for the core source itself before any general movement fallback. Repeated core moves must not scan and flood-fill every earlier cell in insertion order.
- Core initialization cheaply ranks every occupied cell, then runs whole-body removability checks on at most the best 12 candidates. Loading an upgraded large-body save must not perform one flood fill per cell.
- Growth radius eases in; shrink radius eases out.
- Texture/material/buffer resources are transient and must be released on despawn, destroy, map removal, load, shutdown, and main-menu transitions.
- Hot paths must reject unrelated calls before active-Symbiant lookup. Do not scan `map.mapPawns.AllPawns` from Symbiant hot paths.
- Active-Symbiant caches are map-object keyed and transient. They are cleared on load, shutdown, main-menu transitions, and map removal.
- Draw, hover, tooltip, inspection, and ordinary `GrowthState` reads perform no room-capacity scan. Capacity is evaluated only at slow footprint decisions; multiple feed-menu checks in one tick and unchanged shape share one result. Authorized exterior footprints that retain any integrated indoor cell still avoid a capacity scan on the 250-tick symbiosis refresh; a zero-integrated footprint performs the required no-room check, then uses the relocation timer during grace and the longer blocked retry cadence after dormancy.
- Room/roof callbacks perform constant-time invalidation and coalesce into one lazy migration rescan. No mutable room-full decision is persisted or trusted for an irreversible breach; the first overflow transition performs one fresh exact audit, and every continuation rechecks current indoor placement.
- Occupancy work is proportional to the bounded logical footprint. Migration membership uses a transient hash set, and one movement pulse performs at most one queued repair in addition to ordinary movement.
- An impassable-building hook returns after active-map and bounds rejection when unrelated. An intersecting construction batch builds one reusable room candidate plan, then chooses destinations from that plan; work is bounded by the building footprint, relevant-room candidate cells, and covered logical cells rather than repeated whole-map or whole-blob scans.

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
- `zombieland/symbiant_host_effect_isolation_contract` when host health, shared damage, marker hediff, or bond termination changes
- cross-map host-link, shared-damage, and return checks when host lifecycle behavior changed
- clean warning-or-higher logs except documented old-save missing-def compatibility errors
- asset bundle rebuild only if shader/material assets changed
- tracked DLLs restored before a normal source-only commit
