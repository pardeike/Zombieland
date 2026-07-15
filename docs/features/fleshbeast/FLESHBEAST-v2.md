# Fleshmass Displacement Design: V2

## Status And Scope

This is a design contract for a future Zombieland and Anomaly interaction. It
does not claim that the mechanic has been implemented or live-tested. The
unchanged pre-review proposal is retained as [`FLESHBEAST-v1.md`](FLESHBEAST-v1.md)
for rationale and comparison; this document supersedes it as the implementation
specification.

V2 keeps the original fantasy: zombies chew into active fleshmass, the mass
withdraws, and a later visible pulse establishes equivalent active fleshmass
elsewhere. It deliberately narrows V1 so that zombies cannot turn Anomaly's
heart encounter into free cleanup:

- One completed chew retracts exactly one eligible active-fleshmass cell and
  produces exactly one relocation unit for that cell's owning heart.
- A Zombieland-owned, owner-scoped queue controls relocation. Vanilla heart
  growth points and the normal heart `Grow()` path are not part of this loop.
- A full one-cell touch ring around the heart remains non-chewable at all times.
  Zombies must never create the final access route to an analyzed heart.
- The existing zombie attack-mode setting is authoritative. V1 has only a local
  chewing rule and no scent-driven global target acquisition.
- Wounded cells, defender spending, bonus points, and special-zombie modifiers
  are explicitly deferred.

The intended result is pressure and map change, not an alternate heart-kill
path. The player still owns the neural-lump analysis and heart-overload
objective.

## Current Constraints

- `FleshmassHeart` is an Anomaly building, not a pawn. It must not be added to
  normal zombie attack targeting or treated as a hit-point combat target.
- In the current RimWorld 1.6 behavior, ordinary removal of `Fleshmass_Active`
  can run `CompCascadeOnDestroyed` and retract several connected cells. A
  normal damage or `Kill()` call is therefore not an acceptable representation
  of one zombie chew.
- After neural-lump analysis, reaching the heart is enough to start the vanilla
  heart-destruction interaction. A zombie-cleared touch ring would be a
  repeatable bypass of the final encounter, not merely a tactical opportunity.
- `CompGrowsFleshmassTendrils` spends vanilla growth points every few ticks and
  may use them for outputs other than active fleshmass. It cannot implement an
  hourly, one-for-one relocation pulse.
- Vanilla contiguous-mass traversal follows adjacency across related fleshmass
  objects without using `CompFleshmass.source` as an ownership boundary. Two
  fields that touch must remain distinct in this feature.
- The normal `ZombieAttackTargetIndex` and attackability path are pawn-oriented.
  Indexing every active-fleshmass cell as a regular target would be both a
  semantic change and an avoidable scan/pathing cost.

## Design Goals

- Prevent passive containment: a heart left outside should not remain a static,
  harmless background object while zombies are present.
- Preserve the Anomaly objective: zombies never damage, overload, or supply the
  final approach to the heart.
- Preserve the one-cell/one-unit mental model. Zombie pressure relocates mass;
  it does not silently destroy a larger field or mint defenders.
- Keep behavior readable: relocation happens in limited, visible hourly pulses
  rather than as a constant hidden trickle.
- Respect player-selected zombie behavior. `OnlyColonists` and `OnlyHumans`
  must not gain a new non-pawn aggression exception.
- Keep work bounded on constrained maps. Old or unplaceable pressure must not
  become an unlimited historical debt that bursts through a later opening.
- Leave ordinary fleshbeast hostility to the existing Anomaly/Zombieland
  settings; this feature controls only active-fleshmass displacement.

## Recommended V1

Use one map component, `FleshmassDisplacementMapComponent` (name provisional),
with a state record for each tracked heart. A record is keyed by that exact
heart's stable thing identity and is discarded when the heart is gone or no
longer belongs to the map.

Each state record contains only:

- pending relocation units, each with its enqueue tick;
- the next hourly pulse tick; and
- any minimal bookkeeping necessary to find and clean up that exact heart's
  active cells.

There is no wound state, pressure-by-cell accumulator, vanilla growth-point
mutation, defender budget, or special-zombie multiplier in V1. A zombie's
successful dedicated chew retracts an eligible cell immediately and appends one
relocation unit. The later pulse places explicit active cells for the same
heart.

### Ownership And Eligible Cells

An eligible target satisfies every condition below:

- It is a spawned `Fleshmass_Active` on the zombie's current map.
- Its `CompFleshmass.source` is the exact tracked heart, not merely a nearby or
  contiguous heart-like object.
- It is on the reachable outer perimeter of that owner's field according to the
  local chew search.
- It is outside the protected heart-touch ring described below.

Vanilla contiguous-mass information may help find likely perimeter candidates or
valid expansion directions, but it is never the ownership authority. Every
target, removal, and newly placed active cell is checked against the explicit
source heart. This remains true when two heart fields touch.

### Protected Heart-Touch Ring

The complete one-cell ring immediately surrounding the heart's 3-by-3 footprint,
including diagonal cells, is permanently ineligible for zombie chewing and for
any fallback removal rule. This protection applies before and after neural-lump
analysis and is not affected by queue pressure, lack of expansion space, or
attack mode.

Consequences:

- Zombies may displace the outer field but cannot clear the last fleshmass
  barrier needed to touch the heart.
- A player who completes analysis still needs to deal with the normal final
  encounter; zombie-created access cannot start `CompDestroyHeart`.
- V1 does not have an "exposed heart" reward, special state, or defender burst.
  Those ideas may be reconsidered only with a new explicit risk/gate.

## Attack-Mode Contract And Local Chewing

Fleshmass is not added to `ZombieAttackTargetIndex`, `Tools.Attackable`, or the
normal global target-acquisition path. Instead, an explicit local chewing check
may run only for an idle zombie already adjacent to an eligible cell. It runs
after ordinary adjacent pawn targeting has had a chance to win, so it neither
replaces pawn priorities nor pulls zombies across the map.

The existing `attackMode` setting decides whether that local rule is enabled:

| Attack mode | V1 local chewing | Heart scent / attraction |
| --- | --- | --- |
| `OnlyColonists` | Disabled. Zombies do not initiate a fleshmass attack. | Disabled. |
| `OnlyHumans` | Disabled. Fleshmass is not a human pawn. | Disabled. |
| `Everything` | Enabled only for an adjacent, eligible perimeter cell when no normal pawn target was selected. | Not included in V1. |

There is no special Anomaly override for a building. Existing Anomaly targeting
overrides continue to decide pawn-versus-pawn relationships for fleshbeasts and
other entities only.

If a later version adds a heart scent, it must be a small local steering rule,
not a new global fleshmass index. It must be gated by the existing
`attackMode == Everything` setting, must not choose the heart as an attack
target, and must retain ordinary pawn priorities. Adding it requires its own
settings-matrix test; it is not part of this V1.

## Dedicated Chew And Retraction Operation

The operation is a single Zombieland-owned path, conceptually
`TryChewActiveCell(zombie, heart, cell)`. It must make the following guarantees:

1. Revalidate the zombie, map, exact source-heart ownership, active-cell def,
   perimeter status, and protected-ring exclusion immediately before removal.
2. Use the chewing zombie as the instigator/cause. Never use a no-instigator
   `Kill()` that vanilla can classify as player-caused destruction.
3. Suppress or bypass `CompCascadeOnDestroyed` for this operation. One completed
   chew must retract only the selected active cell; it must not cascade through
   four to eight connected cells.
4. Deliberately perform the normal connected-mass cleanup/notification required
   after a single zombie-caused cell removal, exactly once. The implementation
   pass must identify the correct vanilla notification path with decompiler
   evidence rather than assuming that ordinary `Kill()` has safe semantics.
5. Append exactly one relocation unit to the exact source heart's queue only
   after that one-cell retraction succeeds. Failed validation or removal creates
   no unit.

Do not express this as ordinary building damage, a player-caused destruction
callback, or generic environmental sabotage. The operation needs a small,
testable contract because its one-cell accounting is the core of the mechanic.

## Independent Relocation Queue And Hourly Pulse

Pending relocation units are Zombieland state. They must never be written into
the heart's vanilla `growthPoints`, and the pulse must not call or override
`CompFleshmassHeart.Grow()` as its spending mechanism. Vanilla placement rules
are useful inspiration, but the output is always one explicitly chosen and
spawned `Fleshmass_Active` cell with the tracked heart recorded as its source.

At every in-game hour, each heart processes its own queue:

1. Discard units older than the configured expiry.
2. Take at most the configured per-pulse work limit, oldest first.
3. For each unit, select a valid active-cell placement from the exact heart's
   current field using vanilla-compatible terrain, occupancy, and connectivity
   constraints, without using vanilla growth points or non-active outputs.
4. On success, place exactly one active cell and consume the unit.
5. If no valid cell is found in the bounded search, consume the unit as failed
   pressure. Do not leave it queued to burst later.

The queue is also capped per heart. When a completed chew arrives at a full
queue, discard the oldest queued unit first, then record the new one. Thus the
newly retracted cell is credited exactly once while old, unspent pressure is
explicitly lost. This bounds memory and makes current pressure matter more than
ancient blocked attempts.

Suggested initial guardrails, to be tuned in runtime fixtures:

- pulse cadence: one in-game hour;
- maximum queued units per heart: 48;
- maximum successful placements per pulse: 12;
- unit expiry: six in-game hours; and
- a failed bounded placement search consumes its unit immediately.

These limits are not a low daily displacement cap. A large horde can still move
the front substantially, but a constrained frontier cannot save an arbitrary
debt and release it all through one new cell of space.

## Placement Rules

The pulse uses a Zombieland-side candidate selection routine. It may look to
current vanilla tendril/core placement rules for terrain and connectivity
constraints, but it must not:

- spend or add vanilla `growthPoints`;
- call a method that may choose a nerve bundle, spitter, fleshbeast, or any
  other normal heart output;
- join fields or transfer a unit across a different heart's source boundary;
- place an isolated cell; or
- place a protected-ring cell as part of a special fallback.

V1 has no recent-wound avoidance memory. That earlier idea is intentionally
deferred with the wounded-state feature. The safe first proof is equal
displacement with bounded, source-correct placement. Directional bias can be
considered after the core contract has live evidence.

## Deferred Variants

The following ideas from V1 remain plausible but are deliberately outside this
implementation slice:

- wounded or temporarily passable flesh before retraction;
- growth bias away from recent wounds;
- bonus relocation units from sustained attacks or corpses;
- spending displacement on fleshbeasts, spitters, nerve bundles, or any other
  defender output;
- special-zombie pressure modifiers;
- an exposed/convulsing-heart state; and
- heart scent or route attraction.

Each changes either save-state complexity, attack-mode semantics, heart access,
or the one-cell/one-unit accounting. None should enter until the narrow V1 has
been implemented and proven in a combined fixture.

## Player-Facing Outcome

- In `Everything`, a zombie that wanders into an outer fleshmass perimeter may
  chew it when it has no adjacent pawn target. The mass can later reappear as
  active cells elsewhere from the same heart.
- In `OnlyHumans` and `OnlyColonists`, the feature does not introduce a new
  building/environment attack. Nearby zombies ignore the fleshmass unless some
  unrelated system moves them.
- Zombies cannot attack the heart and cannot chew the protective ring, so they
  never create a final-interaction route.
- Normal Anomaly heart behavior still controls its own defenders. Existing
  Zombieland/Anomaly hostility settings still control how those pawns treat
  zombies and vice versa.

The UI is optional for the first code slice, but a later player-facing release
should explain that chewed cells become delayed relocation pressure. It must not
imply that a zombie horde is an intended way to destroy a heart.

## Implementation Boundaries

- Keep the map component owner-scoped. Do not attach global pending pressure to
  a generic contiguous-fleshmass collection.
- Clean up a heart's state when the heart is destroyed, despawned, or changes
  map. A stale thing ID must not credit a later heart.
- Use no global per-cell target index and do not expand the existing pawn index
  to include buildings for this feature.
- Keep normal pawn attack selection intact. Local chewing is a separate check,
  not a widening of `Tools.Attackable`.
- Save/load the queue timestamps, next-pulse tick, and heart identity. On load,
  revalidate every queued record against the spawned heart and its map.
- Keep all temporary diagnostics out of the final feature. Runtime fixtures
  should clean up spawned hearts, active cells, zombies, and component records.

## Verification Plan

### Static And Decompiler Pass

- Confirm the current 1.6 `Fleshmass_Active` destruction and cascade path, and
  identify the exact safe way to remove one cell with a zombie instigator while
  preserving required connected-mass notification.
- Confirm the current `CompDestroyHeart` analysis/access behavior and verify the
  exact 3-by-3 footprint and adjacent touch ring used by the protection check.
- Confirm how an active cell records `CompFleshmass.source` and how a new active
  cell can be associated with one exact heart.
- Confirm the normal growth-point cadence and possible non-active outputs so
  the implementation does not accidentally reuse them.
- Confirm existing attack-mode and target-index behavior before adding the
  local-chew hook.

### Focused Runtime Contracts

1. **Single-chew accounting.** Build a connected field, execute one completed
   chew, and prove that exactly the selected active cell is removed, no cascade
   removes neighbours, one unit is queued, and the normal required cleanup ran.
2. **Source ownership.** Stage two hearts whose fields touch. Chew one active
   cell from each field and prove that each queue and each later replacement
   belongs only to its source heart.
3. **Heart barrier.** Complete neural-lump analysis, place zombies around every
   protected-ring cell, and prove that no chew removes the ring and zombies
   cannot make `CompDestroyHeart` interactable through their work.
4. **Attack modes.** Run `OnlyColonists`, `OnlyHumans`, and `Everything` with
   otherwise identical adjacent zombies. Prove that only `Everything` permits
   the local chew and that none of the modes uses global fleshmass targeting.
5. **Bounded queue.** Fill the queue on a blocked frontier, advance several
   hours, then create one valid opening. Prove expiry, oldest-unit eviction,
   failed-search consumption, and the per-pulse placement maximum prevent a
   burst.
6. **Pulse isolation.** Queue units and advance an hour. Prove every success is
   an explicit active cell, never a vanilla defender or other heart output, and
   vanilla growth-point state was not changed by the feature.
7. **Save/load.** Save before a pulse, reload, and prove that queue timestamps,
   source ownership, cap/expiry behavior, and placement count remain stable
   without duplication.

### Combined Scenario Fixture

Use one reusable map fixture containing two nearby heart fields, a protected
analyzed heart, an outer chewable perimeter, a constrained expansion edge, and
a controlled zombie group. The fixture should run the attack-mode matrix, a
hourly pulse, save/load, and log summary in one sequence. It is the regression
surface for coexistence rather than a collection of permanent one-off bridge
tools.

## Acceptance Criteria For V1

V1 is ready to expand only when all of the following are true in the current
RimWorld build:

- no completed chew removes more or fewer than one active cell;
- each successful retraction creates one source-correct relocation unit;
- no ordinary destruction cascade or player-caused heart response is triggered
  by the chew path;
- no relocation uses vanilla `growthPoints` or produces defenders;
- the heart-touch ring is never a zombie-chew target before or after analysis;
- `OnlyColonists` and `OnlyHumans` retain their non-environmental-attack
  contract;
- `Everything` uses only the local chewing rule, with no map-wide fleshmass
  target index or scent;
- queue capacity, expiry, failed-placement consumption, and per-pulse placement
  limits prevent a later burst; and
- the two-touching-hearts and save/load fixture is log-clean.

Only after this proof should the project revisit wound memory, directional bias,
defender responses, special zombies, or a deliberately gated attraction rule.
